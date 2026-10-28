//! 候选窗口：不抢焦点、置顶的分层窗口，跟随光标，画拼音行与候选列表，四周柔和阴影。
//! 缺省交给青简渲染器出位图再贴（[`super::painter`]），配置 `renderer = "system"` 时走 GDI：绘制在 [`view`]，
//! 配色 / 字体在 [`theme`]。绘制内容在 [`RenderData`]，一行的展示形态在 [`row`]。设计语言对齐 macOS 端。

mod render_data;
pub(crate) mod row;
pub(crate) mod theme;
pub(crate) mod view;
mod wheel;

use std::cell::{Cell, RefCell};
use std::rc::Rc;

use windows::Win32::Foundation::{E_INVALIDARG, HWND, LPARAM, LRESULT, POINT, RECT, WPARAM};
use windows::Win32::Graphics::Gdi::{GetDC, ReleaseDC, ScreenToClient};
use windows::Win32::UI::HiDpi::{GetDpiForSystem, GetDpiForWindow};
use windows::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, DestroyWindow, IDC_ARROW, LoadCursorW, SW_HIDE, SW_SHOWNA,
    ShowWindow, WNDCLASSEXW, WS_EX_LAYERED, WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW, WS_EX_TOPMOST,
    WS_POPUP,
};
use windows::core::{Error, PCWSTR, Result, w};

use qingjian_platform::ThemeMode;
use qingjian_platform::protocol::Frame;

pub(crate) use self::render_data::RenderData;
use self::theme::Theme;
use self::wheel::{WM_CANDIDATE_WHEEL, WheelDelta, WheelHook};
use super::layered::{self, Layered};
use super::monitor;
use super::painter::SharedPainter;
use super::window_class::WindowClass;
use crate::dispatch::CandidateAction;

const CLASS_NAME: PCWSTR = w!("QingjianCandidateWindow");
static CLASS: WindowClass = WindowClass::new();

/// 光标行与候选窗之间的间隙（逻辑像素）。
const CARET_GAP: i32 = 2;

/// 按外观模式解析深浅；`System` 读系统主题。
pub(super) fn resolve_dark(mode: ThemeMode) -> bool {
    match mode {
        ThemeMode::Light => false,
        ThemeMode::Dark => true,
        ThemeMode::System => system_prefers_dark(),
    }
}

/// `HKCU\...\Themes\Personalize\AppsUseLightTheme` 为 0 是深色；读不到当浅色。
fn system_prefers_dark() -> bool {
    windows_registry::CURRENT_USER
        .open(r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
        .and_then(|key| key.get_u32("AppsUseLightTheme"))
        .is_ok_and(|value| value == 0)
}

/// 候选窗口。内容经 `UpdateLayeredWindow` 一次贴上，窗口过程只走默认处理。
struct ClickState {
    frame: Frame,
    session: qingjian_platform::protocol::SessionId,
    bounds: Vec<[f32; 4]>,
    on_click: super::CandidateEvents,
    pressed: Option<(Frame, usize)>,

    wheel: WheelDelta,

    content_bounds: Option<[f32; 4]>,
}
thread_local! { static CLICKS: RefCell<std::collections::HashMap<isize, ClickState>> = RefCell::new(std::collections::HashMap::new()); }

pub(crate) struct CandidateWindow {
    hwnd: HWND,

    /// 绘制内容。
    data: RefCell<RenderData>,

    /// 上次用的 DPI，变了重建字体。
    dpi: Cell<u32>,

    /// 上次解析出的深浅，变了重建配色。
    dark: Cell<bool>,

    /// 上次记进日志的缩放值（窗口 DPI、光标所在显示器 DPI）：变了才再记一条（#146）。
    logged_dpi: Cell<Option<(u32, Option<u32>)>>,

    /// 青简渲染器；`None` 走 GDI。
    painter: SharedPainter,

    wheel_hook: WheelHook,
}

impl CandidateWindow {
    /// 建一个隐藏的候选窗口。
    pub(crate) fn new(painter: SharedPainter, on_click: super::CandidateEvents) -> Result<Self> {
        CLASS.ensure(|| WNDCLASSEXW {
            lpfnWndProc: Some(wndproc),
            hInstance: super::module_handle(),
            hCursor: unsafe { LoadCursorW(None, IDC_ARROW) }.unwrap_or_default(),
            lpszClassName: CLASS_NAME,
            ..Default::default()
        })?;
        let dpi = unsafe { GetDpiForSystem() }.max(96);
        let dark = resolve_dark(ThemeMode::default());
        let data = RefCell::new(RenderData::empty(Rc::new(Theme::new(dpi, dark))));
        // NOACTIVATE：显示时不抢应用焦点。
        let hwnd = unsafe {
            CreateWindowExW(
                WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
                CLASS_NAME,
                w!("青简候选"),
                WS_POPUP,
                0,
                0,
                0,
                0,
                None,
                None,
                Some(super::module_handle()),
                None,
            )?
        };
        CLICKS.with(|states| {
            states.borrow_mut().insert(
                hwnd.0 as isize,
                ClickState {
                    frame: Frame::default(),
                    session: qingjian_platform::protocol::SessionId(0),
                    bounds: Vec::new(),
                    pressed: None,
                    wheel: WheelDelta::default(),
                    content_bounds: None,
                    on_click,
                },
            )
        });
        Ok(Self {
            hwnd,
            data,
            dpi: Cell::new(dpi),
            dark: Cell::new(dark),
            logged_dpi: Cell::new(None),
            painter,
            wheel_hook: WheelHook::default(),
        })
    }

    /// 刷新内容（不定位、不显示）。
    pub(crate) fn set_content(
        &self,
        frame: &Frame,
        session: qingjian_platform::protocol::SessionId,
    ) {
        CLICKS.with(|states| {
            if let Some(state) = states.borrow_mut().get_mut(&(self.hwnd.0 as isize)) {
                if state.session != session || state.frame.preedit != frame.preedit {
                    state.wheel.reset();
                }
                state.frame = frame.clone();
                state.session = session;
                state.bounds.clear();
            }
        });
        self.data.borrow_mut().set(frame);
    }

    /// 按光标矩形定位并显示：贴光标下方（放不下放上方），四周留出阴影。
    pub(crate) fn show(&self, anchor: RECT) {
        self.sync_theme(anchor);
        let rendered = {
            let data = self.data.borrow();
            self.painter.borrow_mut().as_mut().and_then(|painter| {
                painter.render_frame(
                    &data.render_frame(),
                    data.layout,
                    self.dark.get(),
                    self.dpi.get(),
                )
            })
        };
        let updated = match rendered {
            Some(rendered) => {
                let content = (
                    rendered.content_width as i32,
                    rendered.content_height as i32,
                );
                if content.0 <= 0 || content.1 <= 0 {
                    self.hide();
                    return;
                }
                let (content_x, content_y) = place(anchor, content);
                CLICKS.with(|states| {
                    if let Some(state) = states.borrow_mut().get_mut(&(self.hwnd.0 as isize)) {
                        state.bounds.clone_from(&rendered.candidate_bounds);
                        state.content_bounds = Some([
                            rendered.content_x as f32,
                            rendered.content_y as f32,
                            (rendered.content_x + rendered.content_width) as f32,
                            (rendered.content_y + rendered.content_height) as f32,
                        ]);
                    }
                });
                layered::present(
                    self.hwnd,
                    &rendered.pixmap,
                    (
                        content_x - rendered.content_x as i32,
                        content_y - rendered.content_y as i32,
                    ),
                )
            }
            None => self.show_gdi(anchor),
        };
        if updated.is_ok() {
            let _ = unsafe { ShowWindow(self.hwnd, SW_SHOWNA) };
            self.wheel_hook.show(self.hwnd);
        } else {
            self.hide();
        }
    }

    /// GDI 画法：量尺寸、定位、合成。
    fn show_gdi(&self, anchor: RECT) -> Result<()> {
        let margin = layered::shadow_margin(self.dpi.get());
        let content = self.preferred_size();
        if content.0 <= 0 || content.1 <= 0 {
            return Err(Error::from(E_INVALIDARG));
        }
        let (content_x, content_y) = place(anchor, content);
        let data = self.data.borrow();
        let result = layered::composite(
            self.hwnd,
            &Layered {
                content,
                margin,
                win_pos: (content_x - margin, content_y - margin),
                win_size: (content.0 + margin * 2, content.1 + margin * 2),
                background: data.theme.background,
                corner_radius: data.theme.corner_radius,
                paint: &|hdc, client| view::paint(hdc, &data, client),
            },
        );
        CLICKS.with(|states| {
            if let Some(state) = states.borrow_mut().get_mut(&(self.hwnd.0 as isize)) {
                state.content_bounds = Some([
                    margin as f32,
                    margin as f32,
                    (margin + content.0) as f32,
                    (margin + content.1) as f32,
                ]);
                state.bounds = data
                    .hit_bounds
                    .borrow()
                    .iter()
                    .map(|r| {
                        [
                            r[0] + margin as f32,
                            r[1] + margin as f32,
                            r[2] + margin as f32,
                            r[3] + margin as f32,
                        ]
                    })
                    .collect();
            }
        });
        result
    }

    pub(crate) fn hide(&self) {
        self.wheel_hook.hide();
        CLICKS.with(|states| {
            if let Some(state) = states.borrow_mut().get_mut(&(self.hwnd.0 as isize)) {
                state.bounds.clear();
                state.content_bounds = None;
                state.pressed = None;
                state.wheel.reset();
            }
        });
        let _ = unsafe { ShowWindow(self.hwnd, SW_HIDE) };
    }

    /// DPI 或深浅变了就重建主题；每次 `show` 前调。
    ///
    /// DPI 取光标所在显示器的：窗口藏着时改了缩放（或睡眠唤醒后多显示器重排），
    /// `GetDpiForWindow` 会停在旧值，候选字就大小不对（#146）。
    fn sync_theme(&self, anchor: RECT) {
        let caret = POINT {
            x: anchor.left,
            y: anchor.top,
        };
        let monitor_dpi = monitor::dpi_near(caret);
        let window_dpi = unsafe { GetDpiForWindow(self.hwnd) };
        let dpi = match (monitor_dpi, window_dpi) {
            (Some(dpi), _) => dpi,
            (None, 0) => self.dpi.get(),
            (None, dpi) => dpi,
        };
        self.log_dpi(caret, window_dpi, monitor_dpi, dpi);
        let dark = resolve_dark(self.data.borrow().theme_mode);
        if dpi != self.dpi.get() || dark != self.dark.get() {
            self.data.borrow_mut().theme = Rc::new(Theme::new(dpi, dark));
            self.dpi.set(dpi);
            self.dark.set(dark);
        }
    }

    /// 缩放值变了就记一条，多显示器 / 睡眠唤醒的问题从日志里能看出取到的是哪个值（#146）。
    fn log_dpi(&self, caret: POINT, window_dpi: u32, monitor_dpi: Option<u32>, used: u32) {
        if self.logged_dpi.replace(Some((window_dpi, monitor_dpi)))
            == Some((window_dpi, monitor_dpi))
        {
            return;
        }
        tracing::info!(
            window_dpi,
            ?monitor_dpi,
            used,
            system_dpi = unsafe { GetDpiForSystem() },
            caret_x = caret.x,
            caret_y = caret.y,
            "候选窗口缩放值"
        );
    }

    /// 内容需要的大小（不含阴影留白）。
    fn preferred_size(&self) -> (i32, i32) {
        let hdc = unsafe { GetDC(Some(self.hwnd)) };
        let size = view::preferred_size(hdc, &self.data.borrow());
        unsafe { ReleaseDC(Some(self.hwnd), hdc) };
        (size.cx, size.cy)
    }
}

impl Drop for CandidateWindow {
    fn drop(&mut self) {
        self.wheel_hook.hide();
        CLICKS.with(|states| states.borrow_mut().remove(&(self.hwnd.0 as isize)));
        let _ = unsafe { DestroyWindow(self.hwnd) };
    }
}

fn reset_wheel() {
    CLICKS.with(|states| {
        if let Ok(mut states) = states.try_borrow_mut() {
            for state in states.values_mut() {
                state.wheel.reset();
            }
        }
    });
}

fn wheel_hit(hwnd: HWND, mut point: POINT) -> bool {
    if !unsafe { ScreenToClient(hwnd, &mut point) }.as_bool() {
        return false;
    }
    CLICKS.with(|states| {
        states
            .try_borrow()
            .ok()
            .and_then(|states| {
                states.get(&(hwnd.0 as isize)).and_then(|state| {
                    state.content_bounds.map(|r| {
                        !state.bounds.is_empty()
                            && point.x as f32 >= r[0]
                            && (point.x as f32) < r[2]
                            && point.y as f32 >= r[1]
                            && (point.y as f32) < r[3]
                    })
                })
            })
            .unwrap_or(false)
    })
}

/// 内容左上角：贴光标下方，放不下放上方，再放不下贴屏幕内；都夹在所在显示器工作区里。
fn place(anchor: RECT, content: (i32, i32)) -> (i32, i32) {
    let work = monitor::work_area_near(POINT {
        x: anchor.left,
        y: anchor.top,
    });
    let x = anchor
        .left
        .clamp(work.left, (work.right - content.0).max(work.left));
    let below = anchor.bottom + CARET_GAP;
    let above = anchor.top - CARET_GAP - content.1;
    let y = if below + content.1 <= work.bottom {
        below
    } else if above >= work.top {
        above
    } else {
        (work.bottom - content.1).max(work.top)
    };
    (x, y)
}

/// 分层窗口无需 `WM_PAINT`，全交默认处理。
unsafe extern "system" fn wndproc(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    use windows::Win32::UI::WindowsAndMessaging::{
        MA_NOACTIVATE, WM_LBUTTONDOWN, WM_LBUTTONUP, WM_MOUSEACTIVATE, WM_MOUSEWHEEL,
    };
    if msg == WM_MOUSEACTIVATE {
        return LRESULT(MA_NOACTIVATE as isize);
    }
    if msg == WM_CANDIDATE_WHEEL || msg == WM_MOUSEWHEEL {
        let delta = if msg == WM_CANDIDATE_WHEEL {
            wparam.0 as u16 as i16
        } else {
            (wparam.0 >> 16) as u16 as i16
        };
        let _ = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
            CLICKS.with(|states| {
                if let Some(state) = states.borrow_mut().get_mut(&(hwnd.0 as isize))
                    && !state.bounds.is_empty()
                {
                    state.pressed = None;
                    let pages = state.wheel.push(delta);
                    if pages != 0 {
                        (state.on_click)(
                            state.session,
                            state.frame.clone(),
                            CandidateAction::Page(pages),
                        );
                    }
                }
            });
        }));
        return LRESULT(0);
    }
    if msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP {
        let x = (lparam.0 as u16 as i16) as f32;
        let y = ((lparam.0 >> 16) as u16 as i16) as f32;
        let _ = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
            CLICKS.with(|states| {
                if let Some(state) = states.borrow_mut().get_mut(&(hwnd.0 as isize)) {
                    let hit = state
                        .bounds
                        .iter()
                        .position(|r| x >= r[0] && x < r[2] && y >= r[1] && y < r[3]);
                    if msg == WM_LBUTTONDOWN {
                        state.pressed = hit.map(|index| (state.frame.clone(), index));
                    } else if let Some((frame, index)) = state.pressed.take()
                        && Some(index) == hit
                        && frame == state.frame
                    {
                        (state.on_click)(
                            state.session,
                            state.frame.clone(),
                            CandidateAction::Click(index),
                        );
                        state.bounds.clear();
                    }
                }
            })
        }));
        return LRESULT(0);
    }
    unsafe { DefWindowProcW(hwnd, msg, wparam, lparam) }
}

use super::*;
use crate::{FontLibrary, Frame, Layout, Row, Theme};

#[test]
#[cfg(windows)]
fn candidate_rectangles_follow_real_layout_at_150_and_200_percent() {
    let mut renderer = Renderer::new(FontLibrary::system("zh-CN").unwrap());
    for scale in [1.5, 2.0] {
        for layout in [Layout::Vertical, Layout::Horizontal] {
            let frame = Frame {
                rows: vec![
                    Row {
                        index: "1".into(),
                        text: "你好".into(),
                        code: None,
                        annotation: vec![],
                        cloud: false,
                    },
                    Row {
                        index: "2".into(),
                        text: "测试长候选词".into(),
                        code: None,
                        annotation: vec![],
                        cloud: false,
                    },
                ],
                ..Frame::default()
            };
            let output = renderer
                .render(
                    &frame,
                    layout,
                    &Theme::light(),
                    scale,
                    Some(&crate::Shadow::mac_panel()),
                )
                .unwrap();
            assert_eq!(output.candidate_bounds.len(), 2);
            for (i, b) in output.candidate_bounds.iter().enumerate() {
                assert!(b[0] < b[2] && b[1] < b[3]);
                assert!(b[0] >= 0.0 && b[2] <= output.pixmap.width() as f32);
                assert!(b[1] >= 0.0 && b[3] <= output.pixmap.height() as f32);
                let (x, y) = ((b[0] + b[2]) / 2.0, (b[1] + b[3]) / 2.0);
                assert_eq!(
                    output
                        .candidate_bounds
                        .iter()
                        .position(|r| x >= r[0] && x < r[2] && y >= r[1] && y < r[3]),
                    Some(i)
                );
            }
        }
    }
}

//! 候选窗口发回的鼠标操作；只在后台线程修改组句状态。

#[derive(Clone, Copy)]
pub enum CandidateAction {
    Click(usize),

    Page(isize),
}

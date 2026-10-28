//! 高精度滚轮累计到一个刻度才翻页，完整保留同一消息中的多个刻度。

#[derive(Default)]
pub(in crate::ui::candidates) struct WheelDelta(i32);

impl WheelDelta {
    pub(in crate::ui::candidates) fn push(&mut self, delta: i16) -> isize {
        self.0 += i32::from(delta);
        let pages = -(self.0 / 120);
        self.0 %= 120;
        pages as isize
    }

    pub(in crate::ui::candidates) fn reset(&mut self) {
        self.0 = 0;
    }
}

#[cfg(test)]
mod tests {
    use super::WheelDelta;

    #[test]
    fn notches_direction_and_high_resolution_remainder() {
        let mut wheel = WheelDelta::default();
        assert_eq!(wheel.push(-120), 1);
        assert_eq!(wheel.push(120), -1);
        assert_eq!(wheel.push(-360), 3);
        assert_eq!(wheel.push(30), 0);
        assert_eq!(wheel.push(60), 0);
        assert_eq!(wheel.push(30), -1);
        assert_eq!(wheel.push(-60), 0);
        wheel.reset();
        assert_eq!(wheel.push(-60), 0);
        assert_eq!(wheel.push(120), 0);
        assert_eq!(wheel.push(60), -1);
    }
}

const WINDOW_MS: u64 = 60_000;
const COOLDOWN_MS: u64 = 10_000;
const MAX_ATTEMPTS: u32 = 3;

#[derive(Default)]
pub(super) struct Budget {
    pub last: u64,
    pub start: u64,
    pub attempts: u32,
}

impl Budget {
    pub fn take(&mut self, now: u64) -> bool {
        if now < self.start || now.saturating_sub(self.start) >= WINDOW_MS {
            self.start = now;
            self.attempts = 0;
        }
        if self.attempts >= MAX_ATTEMPTS
            || (self.attempts > 0 && now.saturating_sub(self.last) < COOLDOWN_MS)
        {
            return false;
        }
        if self.attempts == 0 {
            self.start = now;
        }
        self.attempts += 1;
        self.last = now;
        true
    }
}

#[cfg(test)]
mod tests {
    use super::Budget;
    #[test]
    fn rapid_retries_and_multiple_hosts_share_three_attempt_limit() {
        let mut b = Budget::default();
        assert!(b.take(100_000));
        for time in [100_001, 101_000, 109_999] {
            assert!(!b.take(time));
        }
        assert!(b.take(110_000));
        assert!(b.take(120_000));
        assert!(!b.take(130_000));
        assert!(!b.take(159_999));
        assert!(b.take(160_000));
    }
    #[test]
    fn persisted_budget_remains_bounded_and_clock_change_recovers() {
        let mut b = Budget {
            last: 110_000,
            start: 100_000,
            attempts: 2,
        };
        assert!(!b.take(111_000));
        assert!(b.take(120_000));
        assert!(!b.take(140_000));
        assert!(b.take(90_000));
        assert!(!b.take(90_001));
    }
}

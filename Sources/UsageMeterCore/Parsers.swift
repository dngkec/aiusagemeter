import Foundation

public enum UsageParsers {
    public static func claude(_ data: Data) throws -> [UsageWindow] {
        let root = try JSONValue.decode(data)
        if let limits = root.value(at: "limits")?.array {
            let parsed = limits.enumerated().compactMap { index, item -> UsageWindow? in
                guard let percent = JSONPicking.number(item, ["percent", "utilization", "used_percent"]) else { return nil }
                let kind = item.value(at: "kind")?.string ?? "limit-\(index)"
                let label: String
                switch kind {
                case "session": label = "Current session"
                case "weekly_all": label = "All models"
                default: label = item.value(at: "scope.model.display_name")?.string ?? kind.replacingOccurrences(of: "_", with: " ").capitalized
                }
                let extraKinds = ["extra_usage", "extra", "overage", "usage_credits"]
                let kindLabel = extraKinds.contains(kind) ? "Extra usage" : label
                return UsageWindow(id: kind, label: kindLabel, used: percent, limit: 100, resetsAt: JSONPicking.date(item, ["resets_at", "reset_at"]))
            }
            var windows = parsed
            windows = merging(windows, extras: extras(root))
            if !windows.isEmpty { return windows }
        }
        let definitions: [(String, String, [String], [String])] = [
            ("session", "Current session", ["five_hour.utilization", "fiveHour.utilization", "session.utilization"], ["five_hour.resets_at", "fiveHour.resetsAt", "session.resets_at"]),
            ("weekly", "All models", ["seven_day.utilization", "sevenDay.utilization", "weekly.utilization"], ["seven_day.resets_at", "sevenDay.resetsAt", "weekly.resets_at"]),
            ("opus", "Opus", ["seven_day_opus.utilization", "opus.utilization"], ["seven_day_opus.resets_at", "opus.resets_at"]),
            ("sonnet", "Sonnet", ["seven_day_sonnet.utilization", "sonnet.utilization"], ["seven_day_sonnet.resets_at", "sonnet.resets_at"]),
        ]
        var windows = definitions.compactMap { id, label, percentPaths, resetPaths -> UsageWindow? in
            guard let percent = JSONPicking.number(root, percentPaths) else { return nil }
            return UsageWindow(id: id, label: label, used: percent, limit: 100, resetsAt: JSONPicking.date(root, resetPaths))
        }
        windows = merging(windows, extras: extras(root))
        guard !windows.isEmpty else { throw UsageMeterError.invalidResponse }
        return windows
    }

    public static func codex(_ data: Data) throws -> [UsageWindow] {
        let root = try JSONValue.decode(data)
        var windows: [UsageWindow] = []
        let sets: [(String, String, [String], [String], [String])] = [
            ("primary", "Current session", ["rate_limit.primary_window.used_percent", "rate_limit.primary.used_percent", "rate_limit.primaryWindow.usedPercent", "primary.used_percent"], ["rate_limit.primary_window.resets_at", "rate_limit.primary.resets_at", "rate_limit.primary_window.reset_at", "rate_limit.primaryWindow.resetAt", "primary.resets_at", "primary.reset_at"], ["rate_limit.primary_window.limit_window_seconds"]),
            ("secondary", "Weekly", ["rate_limit.secondary_window.used_percent", "rate_limit.secondary.used_percent", "rate_limit.secondaryWindow.usedPercent", "secondary.used_percent"], ["rate_limit.secondary_window.resets_at", "rate_limit.secondary.resets_at", "rate_limit.secondary_window.reset_at", "rate_limit.secondaryWindow.resetAt", "secondary.resets_at", "secondary.reset_at"], ["rate_limit.secondary_window.limit_window_seconds"]),
        ]
        for (id, label, usedPaths, resetPaths, _) in sets {
            if let used = JSONPicking.number(root, usedPaths) {
                windows.append(UsageWindow(id: id, label: label, used: used, limit: 100, resetsAt: JSONPicking.date(root, resetPaths)))
            }
        }
        if let used = JSONPicking.number(root, ["credits.used", "credits.used_amount"]), let limit = JSONPicking.number(root, ["credits.limit", "credits.total"]), limit > 0 {
            windows.append(UsageWindow(id: "credits", label: "Credits", used: used, limit: limit, resetsAt: JSONPicking.date(root, ["credits.reset_at", "credits.resets_at"]), kind: .credits))
        } else if let remaining = JSONPicking.number(root, ["credits.remaining", "credits.balance"]), let total = JSONPicking.number(root, ["credits.total", "credits.limit", "credits.grant"]), total > 0 {
            windows.append(UsageWindow(id: "credits", label: "Credits", used: max(0, total - remaining), limit: total, resetsAt: JSONPicking.date(root, ["credits.reset_at", "credits.resets_at"]), kind: .credits))
        }
        windows = merging(windows, extras: extras(root))
        guard !windows.isEmpty else { throw UsageMeterError.invalidResponse }
        return windows
    }

    public static func grok(monthly: Data?, credits: Data?) throws -> [UsageWindow] {
        let roots = try [monthly, credits].compactMap { try $0.map(JSONValue.decode) }
        var output: [UsageWindow] = []
        for root in roots {
            let config = root.value(at: "config")?.nonNull ?? root
            let reset = JSONPicking.date(config, ["currentPeriod.end", "billingPeriodEnd"])
            // A period that publishes no percentage is unknown usage, never 0%,
            // so the on-demand ratio is the only fallback that may stand in.
            let onDemand = JSONPicking.number(config, ["onDemandCap.val", "onDemandCap"]).flatMap { cap -> Double? in
                guard cap > 0, let used = JSONPicking.number(config, ["onDemandUsed.val", "onDemandUsed"]) else { return nil }
                return used / cap * 100
            }
            if let percent = JSONPicking.number(config, ["creditUsagePercent", "usage_percentage", "usagePercent"]) ?? onDemand {
                output.append(UsageWindow(id: "weekly", label: "Weekly", used: percent, limit: 100, resetsAt: reset))
            }
            // The CLI's billing extension reports cents rather than a percentage.
            if let cap = JSONPicking.number(config, ["monthlyLimit.val", "monthlyLimit"]), cap > 0,
               let spent = JSONPicking.number(config, ["usage.totalUsed.val", "usage.totalUsed", "usage.includedUsed.val"]) {
                output.append(UsageWindow(id: "included", label: "Included", used: spent / 100, limit: cap / 100, resetsAt: JSONPicking.date(root, ["billingCycle.billingPeriodEnd"]) ?? reset, kind: .apiCost))
            }
            if let products = config.value(at: "productUsage")?.array {
                for (index, product) in products.enumerated() {
                    guard let percent = JSONPicking.number(product, ["usagePercent", "usage_percentage"]) else { continue }
                    output.append(UsageWindow(id: product.value(at: "product")?.string ?? "product-\(index)", label: product.value(at: "product")?.string ?? "Product", used: percent, limit: 100, resetsAt: JSONPicking.date(config, ["billingPeriodEnd", "currentPeriod.end"])))
                }
            }
            if let limit = JSONPicking.number(root, ["monthlyLimit", "billing.monthlyLimit", "config.monthlyLimit"]), let used = JSONPicking.number(root, ["used", "monthlyUsed", "billing.used", "config.used"]) {
                output.append(UsageWindow(id: "monthly", label: "Monthly", used: used, limit: limit, resetsAt: JSONPicking.date(root, ["billingPeriodEnd", "billing.billingPeriodEnd", "config.billingPeriodEnd"])))
            }
            let remaining = JSONPicking.number(root, ["remainingCredits", "credits.remaining", "balance"])
            let total = JSONPicking.number(root, ["totalCredits", "credits.total", "creditLimit"])
            if let remaining, let total, total > 0 {
                output.append(UsageWindow(id: "credits", label: "Credits", used: max(0, total - remaining), limit: total, resetsAt: JSONPicking.date(root, ["billingPeriodEnd", "credits.expiresAt"]), kind: .credits))
            }
            if let windows = root.value(at: "windows")?.array {
                for (index, item) in windows.enumerated() {
                    guard let limit = JSONPicking.number(item, ["limit", "total"]), let used = JSONPicking.number(item, ["used", "consumed"]) else { continue }
                    output.append(UsageWindow(id: "window-\(index)", label: item.value(at: "name")?.string ?? "Usage", used: used, limit: limit, resetsAt: JSONPicking.date(item, ["resetAt", "resets_at"])))
                }
            }
        }
        output = output.uniquedWindows()
        for root in roots { output = merging(output, extras: extras(root)) }
        guard !output.isEmpty else { throw UsageMeterError.invalidResponse }
        return output
    }

    public static func cursor(_ data: Data) throws -> [UsageWindow] {
        let root = try JSONValue.decode(data)
        var windows: [UsageWindow] = []
        let used = JSONPicking.number(root, ["individualUsage.plan.used", "planUsage.used", "usage.used", "used", "numRequests"])
        let limit = JSONPicking.number(root, ["individualUsage.plan.limit", "planUsage.limit", "usage.limit", "limit", "maxRequestUsage"])
        if let used, let limit { windows.append(UsageWindow(id: "plan", label: "Plan usage", used: used, limit: limit, resetsAt: JSONPicking.date(root, ["billingCycleEnd", "period.billingCycleEnd", "individualUsage.plan.billingCycleEnd"]))) }
        if let pct = JSONPicking.number(root, ["usagePercent", "percentUsed", "totalPercentUsed", "planUsage.totalPercentUsed", "individualUsage.plan.percentUsed"]) { windows.append(UsageWindow(id: "plan", label: "Plan usage", used: pct, limit: 100, resetsAt: JSONPicking.date(root, ["billingCycleEnd", "billingCycleEndMs", "planUsage.billingCycleEnd", "planUsage.billingCycleEndMs", "individualUsage.plan.billingCycleEnd"]))) }
        if windows.isEmpty, let spend = JSONPicking.number(root, ["totalSpendCents", "planUsage.totalSpendCents", "includedSpendCents"]), let allowance = JSONPicking.number(root, ["limitCents", "planUsage.limitCents", "includedLimitCents"]) {
            windows.append(UsageWindow(id: "plan", label: "Plan usage", used: spend, limit: allowance, resetsAt: JSONPicking.date(root, ["billingCycleEnd", "billingCycleEndMs"])))
        }
        if JSONPicking.flag(root, ["onDemand.enabled", "onDemandUsage.enabled", "individualUsage.onDemand.enabled"]) != false {
            if let used = JSONPicking.number(root, ["onDemand.used", "onDemandUsage.used", "individualUsage.onDemand.used"]), let limit = JSONPicking.number(root, ["onDemand.limit", "onDemandUsage.limit", "individualUsage.onDemand.limit"]), limit > 0 {
                windows.append(UsageWindow(id: "on-demand", label: "On-demand", used: used, limit: limit, resetsAt: JSONPicking.date(root, ["billingCycleEnd", "onDemand.billingCycleEnd"]), kind: .extraUsage))
            } else if let used = JSONPicking.number(root, ["onDemand.usedCents", "onDemandUsage.usedCents", "onDemandSpendCents"]), let limit = JSONPicking.number(root, ["onDemand.limitCents", "onDemandUsage.limitCents", "onDemandLimitCents"]), limit > 0 {
                windows.append(UsageWindow(id: "on-demand", label: "On-demand", used: used / 100, limit: limit / 100, resetsAt: JSONPicking.date(root, ["billingCycleEnd", "onDemand.billingCycleEnd"]), kind: .extraUsage))
            } else if let pct = JSONPicking.number(root, ["onDemand.percentUsed", "onDemandUsage.totalPercentUsed", "onDemandUsagePercent"]) {
                windows.append(UsageWindow(id: "on-demand", label: "On-demand", used: pct, limit: 100, resetsAt: JSONPicking.date(root, ["billingCycleEnd"])))
            }
        }
        windows = merging(windows.uniquedWindows(), extras: extras(root))
        guard !windows.isEmpty else { throw UsageMeterError.invalidResponse }
        return windows
    }

    public static func copilot(_ data: Data) throws -> [UsageWindow] {
        let root = try JSONValue.decode(data)
        let reset = JSONPicking.date(root, ["quota_reset_date_utc", "quota_reset_date"])
        var output: [UsageWindow] = []
        if let snapshots = root.value(at: "quota_snapshots")?.object {
            let preference = ["premium_interactions", "chat", "completions"]
            let keys = preference + snapshots.keys.filter { !preference.contains($0) }.sorted()
            for key in keys {
                guard let item = snapshots[key] else { continue }
                // An unlimited quota reports entitlement 0 and 100% remaining;
                // drawing it would put an empty-looking budget on the card.
                if JSONPicking.flag(item, ["unlimited"]) == true { continue }
                let total = JSONPicking.number(item, ["quota_entitlement", "entitlement", "limit", "total"])
                let remaining = JSONPicking.number(item, ["quota_remaining", "remaining"])
                let used = JSONPicking.number(item, ["used"])
                let percentConsumed = JSONPicking.number(item, ["percent_remaining"]).map { max(0, 100 - $0) }
                let extra = key.contains("extra") || key.contains("credit") || key.contains("overage")
                let kind: UsageKind = extra ? .credits : .quota
                let label = extra ? "Credits" : key.replacingOccurrences(of: "_", with: " ").capitalized
                if let percentConsumed {
                    output.append(UsageWindow(id: key, label: label, used: percentConsumed, limit: 100, resetsAt: reset, kind: extra ? .quota : kind))
                } else if let total, let consumed = used ?? remaining.map({ max(0, total - $0) }) {
                    output.append(UsageWindow(id: key, label: label, used: consumed, limit: total, resetsAt: reset, kind: kind))
                }
            }
        }
        output = merging(output, extras: extras(root))
        guard !output.isEmpty else { throw UsageMeterError.invalidResponse }
        return output
    }

    public static func gemini(_ data: Data) throws -> [UsageWindow] {
        let root = try JSONValue.decode(data)
        let buckets = root.value(at: "buckets")?.array ?? root.value(at: "quota.buckets")?.array ?? []
        let output = buckets.enumerated().compactMap { index, item -> UsageWindow? in
            let remaining = JSONPicking.number(item, ["remainingAmount", "remaining", "remainingFraction"])
            let limit = JSONPicking.number(item, ["limit", "total", "maxAmount"]) ?? (remaining.map { $0 <= 1 ? 1 : 0 })
            guard let remaining, let limit, limit > 0 else { return nil }
            let used = remaining <= 1 && limit == 1 ? 1 - remaining : max(0, limit - remaining)
            return UsageWindow(id: item.value(at: "modelId")?.string ?? "bucket-\(index)", label: item.value(at: "displayName")?.string ?? item.value(at: "modelId")?.string ?? "Model quota", used: used, limit: limit, resetsAt: JSONPicking.date(item, ["resetTime", "resetAt"] ))
        }
        let windows = merging(output, extras: extras(root))
        guard !windows.isEmpty else { throw UsageMeterError.invalidResponse }
        return windows
    }

    /// Kimi reports the membership request pool under `usage` and each rate-limit
    /// window under `limits[]`, whose counts live one level down in `detail`. The
    /// web gateway wraps the same pair in `usages[]`, one entry per scope.
    public static func kimi(_ data: Data) throws -> [UsageWindow] {
        let root = try JSONValue.decode(data)
        let container = root.value(at: "usages")?.array?.first ?? root.value(at: "data")?.nonNull ?? root
        var windows: [UsageWindow] = []
        if let pool = container.value(at: "usage")?.nonNull ?? container.value(at: "detail")?.nonNull ?? root.value(at: "usage"), let window = counted(pool, id: "requests", label: "Requests", kind: .credits) {
            windows.append(window)
        }
        for (index, entry) in (container.value(at: "limits")?.array ?? root.value(at: "limits")?.array ?? []).enumerated() {
            let detail = entry.value(at: "detail")?.nonNull ?? entry
            guard let window = counted(detail, id: "limit-\(index)", label: rateWindowLabel(entry.value(at: "window")), kind: .quota) else { continue }
            windows.append(window)
        }
        windows = merging(windows, extras: extras(root) + extras(container))
        guard !windows.isEmpty else { throw UsageMeterError.invalidResponse }
        return windows
    }

    /// Kimi sends its counts as decimal strings, and reports what is left rather
    /// than what a window has consumed when both are not present.
    private static func counted(_ node: JSONValue, id: String, label: String, kind: UsageKind) -> UsageWindow? {
        guard let limit = JSONPicking.number(node, ["limit", "total", "quota"]), limit > 0 else { return nil }
        let used = JSONPicking.number(node, ["used", "usage"]) ?? JSONPicking.number(node, ["remaining"]).map { max(0, limit - $0) }
        guard let used else { return nil }
        return UsageWindow(id: id, label: label, used: used, limit: limit, resetsAt: JSONPicking.date(node, ["resetTime", "reset_at", "resets_at", "resetAt"]), kind: kind)
    }

    private static func rateWindowLabel(_ window: JSONValue?) -> String {
        guard let window, let duration = JSONPicking.number(window, ["duration"]), duration > 0 else { return "Rate limit" }
        let minutes: Double
        switch (window.value(at: "timeUnit")?.string ?? window.value(at: "time_unit")?.string ?? "").uppercased() {
        case "TIME_UNIT_HOUR", "HOUR": minutes = duration * 60
        case "TIME_UNIT_DAY", "DAY": minutes = duration * 1440
        case "TIME_UNIT_SECOND", "SECOND": minutes = duration / 60
        default: minutes = duration
        }
        if minutes >= 1440 { return "\(Int((minutes / 1440).rounded()))-day limit" }
        if minutes >= 60 { return "\(Int((minutes / 60).rounded()))-hour limit" }
        return "\(Int(minutes.rounded()))-minute limit"
    }

    /// The cost report returns one bucket per day, each holding a `results` list,
    /// and every `amount` is a decimal string in the currency's lowest unit —
    /// cents. The month's spend is the sum of every result in every bucket,
    /// converted once at the end; no bucket carries a running total.
    public static func anthropicCost(_ data: Data, monthlyBudget: Double) throws -> [UsageWindow] {
        let root = try JSONValue.decode(data)
        var cents = 0.0
        var found = false
        for bucket in root.value(at: "data")?.array ?? [] {
            for item in bucket.value(at: "results")?.array ?? [] {
                guard let amount = JSONPicking.number(item, ["amount", "cost"]) else { continue }
                cents += amount
                found = true
            }
        }
        if !found, let amount = JSONPicking.number(root, ["total_cost", "amount", "cost"]) {
            cents = amount
            found = true
        }
        guard found else { throw UsageMeterError.invalidResponse }
        return [UsageWindow(id: "monthly-cost", label: "Monthly API cost", used: cents / 100, limit: max(0.01, monthlyBudget), kind: .apiCost)]
    }

    public static func openAICost(_ data: Data, monthlyBudget: Double) throws -> [UsageWindow] {
        let root = try JSONValue.decode(data)
        var total = 0.0
        var found = false
        for bucket in root.value(at: "data")?.array ?? [] {
            let results = bucket.value(at: "results")?.array ?? bucket.value(at: "result")?.array ?? []
            for item in results {
                if let amount = JSONPicking.number(item, ["amount.value", "amount", "cost", "usd"]) {
                    total += amount
                    found = true
                }
            }
        }
        if !found, let amount = JSONPicking.number(root, ["total_cost", "amount", "cost", "total_usage"]) {
            total = amount
            found = true
        }
        guard found else { throw UsageMeterError.invalidResponse }
        return [UsageWindow(id: "monthly-cost", label: "Monthly API cost", used: total, limit: monthlyBudget, kind: .apiCost)]
    }

    public static func openRouter(credits: Data?, key: Data?, monthlyBudget: Double) throws -> [UsageWindow] {
        var windows: [UsageWindow] = []
        if let credits {
            let root = try JSONValue.decode(credits)
            let container = root.value(at: "data")?.nonNull ?? root
            if let total = JSONPicking.number(container, ["total_credits", "totalCredits"]), let used = JSONPicking.number(container, ["total_usage", "totalUsage", "usage"]), total > 0 {
                windows.append(UsageWindow(id: "credits", label: "Credits", used: used, limit: total, kind: .credits))
            }
        }
        if let key {
            let root = try JSONValue.decode(key)
            let container = root.value(at: "data")?.nonNull ?? root
            if let limit = JSONPicking.number(container, ["limit"]), limit > 0 {
                let remaining = JSONPicking.number(container, ["limit_remaining", "limitRemaining"])
                let used = remaining.map { max(0, limit - $0) } ?? JSONPicking.number(container, ["usage_monthly", "usage"]) ?? 0
                windows.append(UsageWindow(id: "key-limit", label: "Key limit", used: used, limit: limit, kind: .apiCost))
            }
            if windows.isEmpty, let monthly = JSONPicking.number(container, ["usage_monthly", "usageMonthly"]) {
                windows.append(UsageWindow(id: "monthly", label: "Monthly API cost", used: monthly, limit: max(0.01, monthlyBudget), kind: .apiCost))
            }
        }
        windows = windows.uniquedWindows()
        guard !windows.isEmpty else { throw UsageMeterError.invalidResponse }
        return windows
    }

    public static func deepSeek(_ data: Data, monthlyBudget: Double) throws -> [UsageWindow] {
        let root = try JSONValue.decode(data)
        let infos = root.value(at: "balance_infos")?.array ?? root.value(at: "balanceInfos")?.array ?? []
        let usd = infos.first { ($0.value(at: "currency")?.string ?? "").uppercased() == "USD" }
        let chosen = usd ?? infos.first ?? root
        guard let remaining = JSONPicking.number(chosen, ["total_balance", "totalBalance", "balance", "remaining"]) else { throw UsageMeterError.invalidResponse }
        return [balance(remaining: remaining, monthlyBudget: monthlyBudget)]
    }

    /// A prepaid balance is not a quota, so it is drawn against the budget the
    /// user expects to spend in a month: an account holding more than that is
    /// not "used up", and one holding nothing is.
    ///
    /// The reading is money, so it is `.apiCost` rather than `.credits`: the
    /// credits caption drops the currency whenever the amount lands on a whole
    /// number, which would make the same balance change format as it is spent.
    static func balance(remaining: Double, monthlyBudget: Double, label: String = "Credits") -> UsageWindow {
        let budget = max(0.01, monthlyBudget)
        if remaining <= 0 { return UsageWindow(id: "credits", label: label, used: budget, limit: budget, kind: .apiCost) }
        if remaining < budget { return UsageWindow(id: "credits", label: label, used: budget - remaining, limit: budget, kind: .apiCost) }
        return UsageWindow(id: "credits", label: label, used: 0, limit: remaining, kind: .apiCost)
    }

    /// xAI's prepaid ledger is an inverted running total in string USD cents:
    /// a $10 top-up posts as `"-1000"`, so remaining credit is the negation.
    public static func xaiBalance(_ data: Data, monthlyBudget: Double) throws -> [UsageWindow] {
        let root = try JSONValue.decode(data)
        guard let cents = JSONPicking.number(root, ["total.val", "total"]) else { throw UsageMeterError.invalidResponse }
        return [balance(remaining: -cents / 100, monthlyBudget: monthlyBudget)]
    }

    public static func moonshot(_ data: Data, monthlyBudget: Double) throws -> [UsageWindow] {
        let root = try JSONValue.decode(data)
        let container = root.value(at: "data")?.nonNull ?? root
        guard let available = JSONPicking.number(container, ["available_balance", "availableBalance"]) else { throw UsageMeterError.invalidResponse }
        return [balance(remaining: available, monthlyBudget: monthlyBudget)]
    }

    /// z.ai reports one entry per plan window. `TOKENS_LIMIT` and `CREDIT_LIMIT`
    /// are Coding Plan windows; `TIME_LIMIT` is the separate MCP lane.
    public static func zai(_ data: Data) throws -> [UsageWindow] {
        let root = try JSONValue.decode(data)
        guard let limits = root.value(at: "data.limits")?.array ?? root.value(at: "limits")?.array else { throw UsageMeterError.invalidResponse }
        var ranked: [(sort: Double, window: UsageWindow)] = []
        for (index, item) in limits.enumerated() {
            let type = item.value(at: "type")?.string ?? ""
            guard ["TOKENS_LIMIT", "CREDIT_LIMIT", "TIME_LIMIT"].contains(type) else { continue }
            // A published count beats the integer percentage, which is rounded.
            let cap = JSONPicking.number(item, ["usage"])
            // Both counts are published and they can disagree after a top-up;
            // the larger one is the safe reading of how much is gone.
            let current = JSONPicking.number(item, ["currentValue"])
            let spent = cap.flatMap { total in JSONPicking.number(item, ["remaining"]).map { max(0, total - $0) } }
            let used = [current, spent].compactMap { $0 }.max()
            let id = "\(type.lowercased())-\(index)"
            let label = zaiWindowLabel(item, type: type)
            let reset = JSONPicking.date(item, ["nextResetTime"])
            // The gauge reads the first window, and the one that runs out first
            // is the shortest; MCP is a separate budget and always sorts last.
            let sort = type == "TIME_LIMIT" ? .greatestFiniteMagnitude : (zaiWindowMinutes(item) ?? Double.greatestFiniteMagnitude / 2)
            if let cap, cap > 0, let used {
                let kind: UsageKind = type == "CREDIT_LIMIT" ? .credits : .quota
                ranked.append((sort, UsageWindow(id: id, label: label, used: used, limit: cap, resetsAt: reset, kind: kind)))
            } else if let percent = JSONPicking.number(item, ["percentage"]) {
                ranked.append((sort, UsageWindow(id: id, label: label, used: percent, limit: 100, resetsAt: reset)))
            }
        }
        guard !ranked.isEmpty else { throw UsageMeterError.invalidResponse }
        return ranked.enumerated()
            .sorted { ($0.element.sort, $0.offset) < ($1.element.sort, $1.offset) }
            .map(\.element.window)
    }

    /// Window length as minutes, from the unit code and count z.ai publishes.
    private static func zaiWindowMinutes(_ item: JSONValue) -> Double? {
        let minutesPerUnit: [Int: Double] = [1: 1440, 3: 60, 5: 1, 6: 10080]
        guard let unit = JSONPicking.number(item, ["unit"]).map({ Int($0) }), let scale = minutesPerUnit[unit],
              let count = JSONPicking.number(item, ["number"]), count > 0 else { return nil }
        return count * scale
    }

    /// A plan can carry a token window and a credit window of the same length,
    /// so the lane has to stay in the label; the window length arrives as a unit
    /// code and a count rather than a duration.
    private static func zaiWindowLabel(_ item: JSONValue, type: String) -> String {
        let lane: String
        switch type {
        case "CREDIT_LIMIT": lane = "Credits"
        case "TIME_LIMIT": lane = "MCP"
        default: lane = "Tokens"
        }
        let units: [Int: String] = [1: "day", 3: "hour", 5: "minute", 6: "week"]
        guard let unit = JSONPicking.number(item, ["unit"]).map({ Int($0) }), let name = units[unit],
              let count = JSONPicking.number(item, ["number"]).map({ Int($0) }), count > 0 else { return lane }
        return "\(lane), \(count) \(name)\(count == 1 ? "" : "s")"
    }

    /// OpenCode reports each window's share consumed and how many seconds are
    /// left, so the reset is relative to the moment of the reading.
    public static func openCodeZen(_ data: Data, now: Date = Date()) throws -> [UsageWindow] {
        let root = try JSONValue.decode(data)
        let container = root.value(at: "usage")?.nonNull ?? root.value(at: "data")?.nonNull ?? root
        let percentKeys = ["usagePercent", "usage_percent", "percentUsed", "used_percent", "percent"]
        let resetKeys = ["resetInSec", "resetInSeconds", "reset_in_sec"]
        let lanes: [(String, String, [String])] = [
            ("rolling", "Session", ["rollingUsage", "rolling", "rolling_usage", "rollingWindow"]),
            ("weekly", "Weekly", ["weeklyUsage", "weekly", "weekly_usage", "weeklyWindow"]),
            ("monthly", "Monthly", ["monthlyUsage", "monthly", "monthly_usage", "monthlyWindow"]),
        ]
        var windows: [UsageWindow] = []
        for (id, label, containerKeys) in lanes {
            guard let lane = containerKeys.lazy.compactMap({ container.value(at: $0) }).first,
                  let percent = JSONPicking.number(lane, percentKeys) else { continue }
            let reset = JSONPicking.number(lane, resetKeys).map { now.addingTimeInterval($0) }
            windows.append(UsageWindow(id: id, label: label, used: percent, limit: 100, resetsAt: reset))
        }
        guard !windows.isEmpty else { throw UsageMeterError.invalidResponse }
        return windows
    }

    /// Warp counts requests, and an unlimited plan reports a zero limit that
    /// would otherwise draw as an empty budget.
    public static func warp(_ data: Data) throws -> [UsageWindow] {
        let root = try JSONValue.decode(data)
        // GraphQL reports its own failures inside a 200, so the message in the
        // body is the only thing that explains a rejected key.
        if let message = root.value(at: "errors.0.message")?.string, !message.isEmpty {
            throw UsageMeterError.setupNeeded("Warp rejected the request: \(message)")
        }
        guard let info = root.value(at: "data.user.user.requestLimitInfo")?.nonNull ?? root.value(at: "data.user.requestLimitInfo")?.nonNull ?? root.value(at: "requestLimitInfo")?.nonNull else {
            throw UsageMeterError.invalidResponse
        }
        if JSONPicking.flag(info, ["isUnlimited"]) == true { throw UsageMeterError.setupNeeded("This Warp plan has no request limit to measure.") }
        guard let limit = JSONPicking.number(info, ["requestLimit"]), limit > 0,
              let used = JSONPicking.number(info, ["requestsUsedSinceLastRefresh", "requestsUsed"]) else { throw UsageMeterError.invalidResponse }
        return [UsageWindow(id: "requests", label: "Requests", used: used, limit: limit, resetsAt: JSONPicking.date(info, ["nextRefreshTime"]), kind: .credits)]
    }

    /// JetBrains IDEs write the assistant's quota to disk themselves: the XML
    /// carries JSON inside its attributes, already entity-decoded by the parser.
    public static func jetBrains(quotaInfo: String, nextRefill: String?) throws -> [UsageWindow] {
        let quota = try JSONValue.decode(Data(quotaInfo.utf8))
        guard let maximum = JSONPicking.number(quota, ["maximum"]), maximum > 0 else { throw UsageMeterError.invalidResponse }
        // `current` is what has been spent; `tariffQuota.available` is what is
        // left, and the two do not always agree after a mid-period top-up.
        let used = JSONPicking.number(quota, ["tariffQuota.available"]).map { max(0, maximum - $0) }
            ?? JSONPicking.number(quota, ["current"])
        guard let used else { throw UsageMeterError.invalidResponse }
        var reset: Date?
        if let nextRefill, let refill = try? JSONValue.decode(Data(nextRefill.utf8)) {
            reset = JSONPicking.date(refill, ["next"])
        }
        // Quotas run to millions of tokens, so the share consumed is the only
        // reading that fits a gauge.
        return [UsageWindow(id: "quota", label: "Current", used: used, limit: maximum, resetsAt: reset ?? JSONPicking.date(quota, ["until"]))]
    }

    /// `GET /v1/admin/spend-limit` already reports the month's spend against the
    /// organization's cap, so it is the whole reading. `/v1/admin/usage` breaks
    /// consumption down per model with no cost total — pricing it needs a rate
    /// card UsageMeter does not carry — so it is not used here.
    public static func mistral(spendLimit: Data, monthlyBudget: Double) throws -> [UsageWindow] {
        let root = try JSONValue.decode(spendLimit)
        let limits = root.value(at: "limits.completion")?.nonNull ?? root.value(at: "limits")?.nonNull ?? root
        guard let used = JSONPicking.number(limits, ["total_usage", "usage"]) else { throw UsageMeterError.invalidResponse }
        // An organization with no cap set is measured against its own budget.
        let uncapped = JSONPicking.flag(limits, ["no_monthly_limit"]) == true
        let cap = uncapped ? nil : JSONPicking.number(limits, ["usage_limit"])
        return [UsageWindow(id: "monthly-cost", label: "Monthly API cost", used: used, limit: max(0.01, cap ?? monthlyBudget), kind: .apiCost)]
    }

    public static func custom(_ data: Data, connector: CustomConnector) throws -> [UsageWindow] {
        let root = try JSONValue.decode(data)
        let reset = DateParser.parse(root.value(at: connector.resetPath))
        let haystack = (connector.name + " " + connector.usedPath + " " + connector.limitPath + " " + connector.percentPath).lowercased()
        let kind: UsageKind = haystack.contains("credit") ? .credits : (haystack.contains("cost") || haystack.contains("spend") ? .apiCost : .quota)
        var windows: [UsageWindow] = []
        if let percent = root.value(at: connector.percentPath)?.double {
            windows = [UsageWindow(id: "custom", label: connector.name, used: percent, limit: 100, resetsAt: reset, kind: kind)]
        } else {
            guard let used = root.value(at: connector.usedPath)?.double else { throw UsageMeterError.missingField(connector.usedPath) }
            guard let limit = root.value(at: connector.limitPath)?.double else { throw UsageMeterError.missingField(connector.limitPath) }
            windows = [UsageWindow(id: "custom", label: connector.name, used: used, limit: limit, resetsAt: reset, kind: kind)]
        }
        return merging(windows, extras: extras(root))
    }

    /// Extra usage and credits that appear on any provider payload, omitted when absent.
    private static func extras(_ root: JSONValue) -> [UsageWindow] {
        var windows: [UsageWindow] = []
        if let extra = claudeExtra(root) { windows.append(extra) }
        if let used = JSONPicking.number(root, ["credits.used", "credits.used_amount", "credit.used"]), let limit = JSONPicking.number(root, ["credits.limit", "credits.total", "credit.limit", "credit.total"]), limit > 0 {
            windows.append(UsageWindow(id: "credits", label: "Credits", used: used, limit: limit, resetsAt: JSONPicking.date(root, ["credits.reset_at", "credits.resets_at"]), kind: .credits))
        } else if let remaining = JSONPicking.number(root, ["credits.remaining", "credits.balance", "credit.remaining"]), let total = JSONPicking.number(root, ["credits.total", "credits.limit", "credit.total"]), total > 0 {
            windows.append(UsageWindow(id: "credits", label: "Credits", used: max(0, total - remaining), limit: total, resetsAt: JSONPicking.date(root, ["credits.reset_at"]), kind: .credits))
        }
        return windows
    }

    private static func merging(_ windows: [UsageWindow], extras: [UsageWindow]) -> [UsageWindow] {
        var output = windows
        for extra in extras where !output.contains(where: { $0.id == extra.id }) {
            output.append(extra)
        }
        return output
    }

    /// Extra usage is omitted unless the account has it enabled and reports a cap.
    private static func claudeExtra(_ root: JSONValue) -> UsageWindow? {
        let extra = root.value(at: "extra_usage") ?? root.value(at: "extraUsage")
        guard let extra else { return nil }
        if JSONPicking.flag(extra, ["is_enabled", "enabled"]) == false { return nil }
        guard let used = JSONPicking.number(extra, ["used_credits", "usedCredits", "used", "amount"]),
              let limit = JSONPicking.number(extra, ["monthly_limit", "monthlyLimit", "limit"]), limit > 0 else {
            // An enabled account can report the share consumed and nothing else.
            guard let percent = JSONPicking.number(extra, ["utilization", "used_percent"]) else { return nil }
            return UsageWindow(id: "extra_usage", label: "Extra usage", used: percent, limit: 100)
        }
        let (dollarsUsed, dollarsLimit) = dollarsIfMinorUnits(used, limit)
        return UsageWindow(id: "extra_usage", label: "Extra usage", used: dollarsUsed, limit: dollarsLimit, kind: .extraUsage)
    }

    private static func dollarsIfMinorUnits(_ used: Double, _ limit: Double) -> (Double, Double) {
        if limit >= 1_000, used == used.rounded(), limit == limit.rounded() { return (used / 100, limit / 100) }
        return (used, limit)
    }
}

private extension Array where Element == UsageWindow {
    func uniquedWindows() -> [UsageWindow] {
        var seen = Set<String>()
        return filter { seen.insert($0.id).inserted }
    }
}

/*
 * date-range-picker.js - a calendar pop-up for choosing a From/To date range.
 *
 * Markup (see Dashboard.cshtml):
 *   <div class="dropdown" data-date-range>
 *       <input type="hidden" id="..." data-range-from min="2000-01-01" max="2026-10-14" value="..." />
 *       <input type="hidden" id="..." data-range-to max="2026-10-14" value="..." />
 *       <button type="button" class="btn ..." data-bs-toggle="dropdown" data-bs-auto-close="outside">
 *           <span data-range-label></span>
 *       </button>
 *       <div class="dropdown-menu date-range-calendar"></div>
 *   </div>
 *
 * The hidden inputs hold the chosen dates (yyyy-MM-dd), so page scripts keep reading them as before.
 * Pressing OK updates both inputs, fires "change" on them and submits the surrounding form.
 * When a page script changes the inputs itself, it should fire "change" so the button label updates.
 */
(() => {
    const WEEKDAYS = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];
    const DAY_CELLS = 42; // 6 weeks, so every month fits

    const toIso = (date) =>
        `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
    const fromIso = (iso) => new Date(`${iso}T00:00:00`);
    const firstOfMonth = (iso) => fromIso(`${iso.slice(0, 7)}-01`);
    const monthTitle = (date) => date.toLocaleDateString("en-MY", { month: "long", year: "numeric" });
    const longDate = (iso) => fromIso(iso).toLocaleDateString("en-MY", { weekday: "long", day: "numeric", month: "long", year: "numeric" });
    const daysBetween = (startIso, endIso) => Math.round((fromIso(endIso) - fromIso(startIso)) / 86400000) + 1;

    const rangeText = (startIso, endIso) => {
        const start = AdminUI.formatDate(fromIso(startIso));
        const end = AdminUI.formatDate(fromIso(endIso));
        return start === end ? start : `${start} - ${end}`;
    };

    const setUp = (root) => {
        const fromInput = root.querySelector("[data-range-from]");
        const toInput = root.querySelector("[data-range-to]");
        const label = root.querySelector("[data-range-label]");
        const panel = root.querySelector(".dropdown-menu");
        const toggle = root.querySelector("[data-bs-toggle=dropdown]");
        const minDate = fromInput.min || "2000-01-01";
        // The pages set max to the restaurant's today, which can differ from the browser's own date.
        const maxDate = toInput.max || fromInput.max || toIso(new Date());
        const today = maxDate;

        // What the user is picking while the calendar is open. Nothing is saved until OK.
        let start = null;
        let end = null;
        let shownMonth = firstOfMonth(maxDate);
        let view = "days";

        const showLabel = () => {
            label.textContent = rangeText(fromInput.value, toInput.value);
        };

        const dayButton = (date) => {
            const iso = toIso(date);
            const inMonth = date.getMonth() === shownMonth.getMonth();
            const isEdge = iso === start || iso === end;
            const inRange = start && end && iso > start && iso < end;
            const outOfBounds = iso < minDate || iso > maxDate;

            const classes = ["btn", "btn-sm", "calendar-day", "p-0", "rounded-3", "border-0"];
            if (isEdge) classes.push("btn-dark");
            else if (inRange) classes.push("bg-body-secondary");
            if (!inMonth && !isEdge) classes.push("text-body-secondary", "opacity-50");
            if (iso === today && !isEdge) classes.push("fw-bold", "text-decoration-underline");

            return `<button type="button" class="${classes.join(" ")}" data-day="${iso}"
                ${outOfBounds ? "disabled" : ""} aria-label="${longDate(iso)}" aria-pressed="${isEdge || Boolean(inRange)}">${date.getDate()}</button>`;
        };

        const hint = () => {
            if (!start) return "Choose the first day";
            if (!end) return "Now choose the last day";
            const days = daysBetween(start, end);
            return `${rangeText(start, end)} (${days} day${days === 1 ? "" : "s"})`;
        };

        const daysView = () => {
            const gridStart = new Date(shownMonth);
            gridStart.setDate(1 - shownMonth.getDay());
            const days = Array.from({ length: DAY_CELLS }, (_, i) => {
                const date = new Date(gridStart);
                date.setDate(gridStart.getDate() + i);
                return dayButton(date);
            }).join("");

            const atFirstMonth = toIso(shownMonth) <= toIso(firstOfMonth(minDate));
            const atLastMonth = toIso(shownMonth) >= toIso(firstOfMonth(maxDate));

            return `
                <div class="d-flex align-items-center justify-content-between gap-2">
                    <button type="button" class="btn btn-sm btn-light border" data-action="previous" aria-label="Previous month" ${atFirstMonth ? "disabled" : ""}>
                        <i class="fa-solid fa-chevron-left"></i>
                    </button>
                    <button type="button" class="btn btn-sm btn-link text-body fw-semibold text-decoration-none" data-action="years" aria-label="Choose a year">
                        ${monthTitle(shownMonth)}<i class="fa-solid fa-caret-down small ms-1"></i>
                    </button>
                    <button type="button" class="btn btn-sm btn-light border" data-action="next" aria-label="Next month" ${atLastMonth ? "disabled" : ""}>
                        <i class="fa-solid fa-chevron-right"></i>
                    </button>
                </div>
                <hr class="my-2" />
                <div class="calendar-grid small text-body-secondary text-center mb-1" aria-hidden="true">
                    ${WEEKDAYS.map(day => `<span>${day}</span>`).join("")}
                </div>
                <div class="calendar-grid">${days}</div>
                <hr class="my-2" />
                <p class="small text-body-secondary text-center mb-2" aria-live="polite">${hint()}</p>
                <div class="d-flex gap-2">
                    <button type="button" class="btn btn-light border flex-fill" data-action="cancel">Cancel</button>
                    <button type="button" class="btn btn-dark flex-fill" data-action="ok" ${start ? "" : "disabled"}>OK</button>
                </div>`;
        };

        // The shown year is large; years further away get smaller and lighter.
        const yearsView = () => {
            const shownYear = shownMonth.getFullYear();
            const years = [];
            for (let year = Number(maxDate.slice(0, 4)); year >= Number(minDate.slice(0, 4)); year--) {
                const distance = Math.abs(year - shownYear);
                const style = distance === 0 ? "fs-4 fw-semibold text-body"
                    : distance === 1 ? "fw-medium text-body"
                    : distance === 2 ? "small text-body-secondary"
                    : "small text-body-tertiary";
                years.push(`<button type="button" class="btn btn-link text-decoration-none d-block w-100 py-1 ${style}" data-year="${year}"
                    aria-pressed="${distance === 0}">${year}</button>`);
            }

            return `
                <div class="d-flex align-items-center justify-content-between gap-2">
                    <button type="button" class="btn btn-sm btn-light border" data-action="days" aria-label="Back to the calendar">
                        <i class="fa-solid fa-chevron-left"></i>
                    </button>
                    <span class="fw-semibold">Year</span>
                    <span class="btn btn-sm invisible" aria-hidden="true"><i class="fa-solid fa-chevron-left"></i></span>
                </div>
                <hr class="my-2" />
                <div class="calendar-years text-center">${years.join("")}</div>`;
        };

        // Redrawing replaces every button, so focus goes back to the same button afterwards.
        // Without this, keyboard users lose their place and Escape no longer closes the calendar.
        const render = () => {
            const focused = panel.contains(document.activeElement) ? document.activeElement : null;
            const focusKey = focused && ["day", "year", "action"].map(key => focused.dataset[key] && `[data-${key}="${focused.dataset[key]}"]`).find(Boolean);

            panel.innerHTML = view === "days" ? daysView() : yearsView();

            const selectedYear = panel.querySelector("[data-year][aria-pressed=true]");
            selectedYear?.scrollIntoView({ block: "center" });
            if (focused) {
                const target = (focusKey && panel.querySelector(focusKey)) || selectedYear || panel.querySelector("button:not([disabled])");
                target?.focus();
            }
        };

        const moveMonth = (months) => {
            shownMonth = new Date(shownMonth.getFullYear(), shownMonth.getMonth() + months, 1);
            render();
        };

        const pickDay = (iso) => {
            if (!start || end) {
                start = iso;
                end = null;
            } else if (iso < start) {
                end = start;
                start = iso;
            } else {
                end = iso;
            }
            shownMonth = firstOfMonth(iso);
            render();
        };

        const pickYear = (year) => {
            const month = String(shownMonth.getMonth() + 1).padStart(2, "0");
            let wanted = `${year}-${month}-01`;
            if (wanted > maxDate) wanted = maxDate;
            if (wanted < minDate) wanted = minDate;
            shownMonth = firstOfMonth(wanted);
            view = "days";
            render();
        };

        const close = () => bootstrap.Dropdown.getOrCreateInstance(toggle).hide();

        const save = () => {
            fromInput.value = start;
            toInput.value = end ?? start;
            fromInput.dispatchEvent(new Event("change"));
            toInput.dispatchEvent(new Event("change"));
            close();
            root.closest("form")?.requestSubmit();
        };

        // Each time the calendar opens it starts from the saved range.
        toggle.addEventListener("show.bs.dropdown", () => {
            start = fromInput.value;
            end = toInput.value;
            shownMonth = firstOfMonth(end || maxDate);
            view = "days";
            render();
        });

        panel.addEventListener("click", (e) => {
            const button = e.target.closest("button");
            if (!button || button.disabled) return;

            if (button.dataset.day) pickDay(button.dataset.day);
            else if (button.dataset.year) pickYear(Number(button.dataset.year));
            else if (button.dataset.action === "previous") moveMonth(-1);
            else if (button.dataset.action === "next") moveMonth(1);
            else if (button.dataset.action === "years") { view = "years"; render(); }
            else if (button.dataset.action === "days") { view = "days"; render(); }
            else if (button.dataset.action === "cancel") close();
            else if (button.dataset.action === "ok") save();
        });

        fromInput.addEventListener("change", showLabel);
        toInput.addEventListener("change", showLabel);
        showLabel();
    };

    document.querySelectorAll("[data-date-range]").forEach(setUp);
})();

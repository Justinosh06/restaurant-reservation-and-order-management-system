/*
 * date-time-picker.js - a calendar pop-up for choosing one date and a time.
 * Looks like the dashboard's date range calendar (date-range-picker.js, .date-range-calendar in admin-pages.css).
 *
 * Markup (see Reservations/Reservation.cshtml):
 *   <div class="dropdown" data-date-time>
 *       <input type="hidden" name="..." data-date-time-value value="2026-10-23T13:00" />
 *       <button type="button" data-bs-toggle="dropdown" data-bs-auto-close="outside">
 *           <span data-date-time-label></span>
 *       </button>
 *       <div class="dropdown-menu date-range-calendar"></div>
 *   </div>
 *
 * The hidden input holds the chosen date and time (yyyy-MM-ddTHH:mm) and is what the form posts.
 * Days before today can't be picked, except the date already saved (editing an older booking).
 */
(() => {
    const WEEKDAYS = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];
    const DAY_CELLS = 42; // 6 weeks, so every month fits
    const TIME_STEP = 15; // minutes between the times offered

    const pad = (number) => String(number).padStart(2, "0");
    const toIso = (date) => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
    const fromIso = (iso) => new Date(`${iso}T00:00:00`);
    const firstOfMonth = (iso) => fromIso(`${iso.slice(0, 7)}-01`);
    const monthTitle = (date) => date.toLocaleDateString("en-MY", { month: "long", year: "numeric" });
    const longDate = (iso) => fromIso(iso).toLocaleDateString("en-MY", { weekday: "long", day: "numeric", month: "long", year: "numeric" });
    const shortDate = (iso) => fromIso(iso).toLocaleDateString("en-MY", { weekday: "short", day: "numeric", month: "short", year: "numeric" });
    const timeText = (time) => {
        const [hours, minutes] = time.split(":").map(Number);
        return `${hours % 12 || 12}:${pad(minutes)} ${hours < 12 ? "AM" : "PM"}`;
    };

    const setUp = (root) => {
        const input = root.querySelector("[data-date-time-value]");
        const label = root.querySelector("[data-date-time-label]");
        const panel = root.querySelector(".dropdown-menu");
        const toggle = root.querySelector("[data-bs-toggle=dropdown]");
        const today = toIso(new Date());

        // What the user is picking while the calendar is open. Nothing is saved until OK.
        let day = null;
        let time = null;
        let shownMonth = firstOfMonth(today);

        const savedDay = () => input.value.slice(0, 10);
        const savedTime = () => input.value.slice(11, 16);
        const minDate = () => (savedDay() && savedDay() < today ? savedDay() : today);

        const showLabel = () => {
            label.textContent = input.value ? `${shortDate(savedDay())}, ${timeText(savedTime())}` : "Choose date and time";
        };

        const times = () => {
            const list = [];
            for (let minutes = 0; minutes < 24 * 60; minutes += TIME_STEP) {
                list.push(`${pad(Math.floor(minutes / 60))}:${pad(minutes % 60)}`);
            }
            if (time && !list.includes(time)) {
                list.push(time);
                list.sort();
            }
            return list;
        };

        const dayButton = (date) => {
            const iso = toIso(date);
            const inMonth = date.getMonth() === shownMonth.getMonth();
            const isPicked = iso === day;
            const classes = ["btn", "btn-sm", "calendar-day", "p-0", "rounded-3", "border-0"];
            if (isPicked) classes.push("btn-dark");
            if (!inMonth && !isPicked) classes.push("text-body-secondary", "opacity-50");
            if (iso === today && !isPicked) classes.push("fw-bold", "text-decoration-underline");

            return `<button type="button" class="${classes.join(" ")}" data-day="${iso}"
                ${iso < minDate() ? "disabled" : ""} aria-label="${longDate(iso)}" aria-pressed="${isPicked}">${date.getDate()}</button>`;
        };

        const hint = () => (day ? `${shortDate(day)}, ${timeText(time)}` : "Choose a day");

        const render = () => {
            const focused = panel.contains(document.activeElement) ? document.activeElement : null;
            const focusKey = focused && (focused.dataset.day ? `[data-day="${focused.dataset.day}"]`
                : focused.dataset.time ? `[data-time="${focused.dataset.time}"]`
                : focused.dataset.action ? `[data-action="${focused.dataset.action}"]` : null);

            const gridStart = new Date(shownMonth);
            gridStart.setDate(1 - shownMonth.getDay());
            const days = Array.from({ length: DAY_CELLS }, (_, i) => {
                const date = new Date(gridStart);
                date.setDate(gridStart.getDate() + i);
                return dayButton(date);
            }).join("");
            const atFirstMonth = toIso(shownMonth) <= toIso(firstOfMonth(minDate()));

            const listScroll = panel.querySelector(".calendar-times")?.scrollTop;

            // Calendar on the left, the times beside it, so both fit on a laptop screen.
            panel.innerHTML = `
                <div class="date-time-columns">
                    <div class="date-time-days">
                        <div class="d-flex align-items-center justify-content-between gap-2">
                            <button type="button" class="btn btn-sm btn-light border" data-action="previous" aria-label="Previous month" ${atFirstMonth ? "disabled" : ""}>
                                <i class="fa-solid fa-chevron-left"></i>
                            </button>
                            <span class="fw-semibold">${monthTitle(shownMonth)}</span>
                            <button type="button" class="btn btn-sm btn-light border" data-action="next" aria-label="Next month">
                                <i class="fa-solid fa-chevron-right"></i>
                            </button>
                        </div>
                        <hr class="my-2" />
                        <div class="calendar-grid small text-body-secondary text-center mb-1" aria-hidden="true">
                            ${WEEKDAYS.map(name => `<span>${name}</span>`).join("")}
                        </div>
                        <div class="calendar-grid">${days}</div>
                    </div>
                    <div class="date-time-times">
                        <div class="fw-semibold text-center">Time</div>
                        <hr class="my-2" />
                        <div class="calendar-times" role="listbox" aria-label="Time">
                            ${times().map(value => `<button type="button" class="btn btn-sm rounded-3 border-0 ${value === time ? "btn-dark" : ""}"
                                data-time="${value}" role="option" aria-selected="${value === time}">${timeText(value)}</button>`).join("")}
                        </div>
                    </div>
                </div>
                <hr class="my-2" />
                <p class="small text-body-secondary text-center mb-2" aria-live="polite" data-hint>${hint()}</p>
                <div class="d-flex gap-2">
                    <button type="button" class="btn btn-light border flex-fill" data-action="cancel">Cancel</button>
                    <button type="button" class="btn btn-dark flex-fill" data-action="ok" ${day ? "" : "disabled"}>OK</button>
                </div>`;

            // Picking a day redraws everything; the time list stays where it was scrolled.
            if (listScroll !== undefined) {
                panel.querySelector(".calendar-times").scrollTop = listScroll;
            }

            // Redrawing replaces every button, so focus goes back to the same one.
            if (focused) {
                (focusKey && panel.querySelector(focusKey) || panel.querySelector("button:not([disabled])"))?.focus();
            }
        };

        // Picking a time only restyles the buttons, so the list keeps its place and focus.
        const pickTime = (button) => {
            time = button.dataset.time;
            panel.querySelectorAll("[data-time]").forEach(option => {
                const isChosen = option === button;
                option.classList.toggle("btn-dark", isChosen);
                option.setAttribute("aria-selected", isChosen);
            });
            panel.querySelector("[data-hint]").textContent = hint();
        };

        const moveMonth = (months) => {
            shownMonth = new Date(shownMonth.getFullYear(), shownMonth.getMonth() + months, 1);
            render();
        };

        // The calendar always opens below the field (no flipping above it). "fixed" keeps the side
        // panel's scrolling area from cutting it off; if it runs past the bottom of the window,
        // it scrolls itself (see "shown.bs.dropdown" below).
        const dropdown = bootstrap.Dropdown.getOrCreateInstance(toggle, {
            popperConfig: (defaults) => ({
                ...defaults,
                strategy: "fixed",
                placement: "bottom-end",
                modifiers: [...(defaults.modifiers ?? []), { name: "flip", enabled: false }]
            })
        });
        const close = () => dropdown.hide();

        const save = () => {
            input.value = `${day}T${time}`;
            input.dispatchEvent(new Event("change"));
            close();
        };

        // Each time the calendar opens it starts from the saved date and time.
        toggle.addEventListener("show.bs.dropdown", () => {
            day = savedDay() || null;
            time = savedTime() || "12:00";
            shownMonth = firstOfMonth(day || today);
            panel.innerHTML = "";
            render();
        });

        // Once the calendar is on screen, scroll the chosen time into the middle of the list.
        // If it runs past the bottom of the window, it scrolls itself.
        toggle.addEventListener("shown.bs.dropdown", () => {
            // Measured from the field: the pop-up may not have moved below it yet.
            panel.style.maxHeight = `${Math.max(240, window.innerHeight - toggle.getBoundingClientRect().bottom - 12)}px`;
            const list = panel.querySelector(".calendar-times");
            const chosen = list.querySelector("[aria-selected=true]");
            if (chosen) {
                list.scrollTop = chosen.offsetTop - list.offsetTop - (list.clientHeight - chosen.offsetHeight) / 2;
            }
        });

        panel.addEventListener("click", (e) => {
            const button = e.target.closest("button");
            if (!button || button.disabled) return;

            if (button.dataset.day) {
                day = button.dataset.day;
                shownMonth = firstOfMonth(day);
                render();
            }
            else if (button.dataset.time) pickTime(button);
            else if (button.dataset.action === "previous") moveMonth(-1);
            else if (button.dataset.action === "next") moveMonth(1);
            else if (button.dataset.action === "cancel") close();
            else if (button.dataset.action === "ok") save();
        });

        input.addEventListener("change", showLabel);
        showLabel();
    };

    document.querySelectorAll("[data-date-time]").forEach(setUp);
})();

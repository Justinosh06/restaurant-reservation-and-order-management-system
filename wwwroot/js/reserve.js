document.addEventListener("DOMContentLoaded", function () {
    const form = document.getElementById("reserveForm");
    const grid = document.getElementById("calendarGrid");
    const monthLabel = document.getElementById("monthLabel");
    const prevMonth = document.getElementById("prevMonth");
    const nextMonth = document.getElementById("nextMonth");
    const dayLabel = document.getElementById("dayLabel");
    const timeList = document.getElementById("timeList");
    const dateInput = document.getElementById("dateInput");
    const timeInput = document.getElementById("timeInput");
    const guestsInput = document.getElementById("guestsInput");
    const summary = document.getElementById("summary");
    const bookButton = document.getElementById("bookButton");
    const slotCount = document.getElementById("slotCount");
    const dateValue = document.getElementById("dateValue");
    const timeValue = document.getElementById("timeValue");

    if (!form || !grid || !timeList || !dateInput || !timeInput || !guestsInput) {
        console.error("Reservation page is missing required elements.");
        return;
    }

    const today = parseDate(form.dataset.today);
    const firstDay = parseDate(form.dataset.firstDay);
    const lastDay = parseDate(form.dataset.lastDay);
    const closedDays = form.dataset.closedDays ? form.dataset.closedDays.split(" ").map(Number) : [];
    const maxGuests = Number(guestsInput.max);
    let clock = 12;
    let shownMonth = new Date(today.getFullYear(), today.getMonth(), 1);
    let slotRequest = 0;

    // Dates are handled as local "yyyy-mm-dd" strings so time zones never shift the day.
    function parseDate(value) {
        const parts = value.split("-").map(Number);
        return new Date(parts[0], parts[1] - 1, parts[2]);
    }

    function formatDate(date) {
        return date.getFullYear() + "-" + String(date.getMonth() + 1).padStart(2, "0")
            + "-" + String(date.getDate()).padStart(2, "0");
    }

    function formatTime(value) {
        if (clock === 24) {
            return value;
        }
        const [hours, minutes] = value.split(":").map(Number);
        return ((hours + 11) % 12 + 1) + ":" + String(minutes).padStart(2, "0") + (hours < 12 ? "am" : "pm");
    }

    function isBookable(date) {
        return date >= firstDay && date <= lastDay && !closedDays.includes(date.getDay());
    }

    function renderCalendar() {
        monthLabel.innerHTML = "";
        const month = document.createElement("strong");
        month.textContent = shownMonth.toLocaleDateString(undefined, { month: "long" });
        monthLabel.append(month, " " + shownMonth.getFullYear());

        prevMonth.disabled = shownMonth <= new Date(today.getFullYear(), today.getMonth(), 1);
        nextMonth.disabled = new Date(shownMonth.getFullYear(), shownMonth.getMonth() + 1, 1) > lastDay;

        grid.replaceChildren();
        for (let i = 0; i < shownMonth.getDay(); i++) {
            grid.appendChild(document.createElement("span"));
        }

        const days = new Date(shownMonth.getFullYear(), shownMonth.getMonth() + 1, 0).getDate();
        for (let day = 1; day <= days; day++) {
            const date = new Date(shownMonth.getFullYear(), shownMonth.getMonth(), day);
            const value = formatDate(date);
            const button = document.createElement("button");
            button.type = "button";
            button.className = "calendar-day";
            button.textContent = day;
            button.dataset.date = value;
            button.disabled = !isBookable(date);
            button.classList.toggle("today", value === formatDate(today));
            button.classList.toggle("selected", value === dateInput.value);
            button.setAttribute("aria-label", date.toLocaleDateString(undefined, { weekday: "long", day: "numeric", month: "long" }));
            button.setAttribute("aria-pressed", value === dateInput.value ? "true" : "false");
            button.addEventListener("click", function () {
                selectDate(value);
            });
            grid.appendChild(button);
        }
    }

    function selectDate(value) {
        if (dateInput.value !== value) {
            timeInput.value = "";
        }
        dateInput.value = value;
        renderCalendar();

        const date = parseDate(value);
        dayLabel.innerHTML = "";
        const weekday = document.createElement("strong");
        weekday.textContent = date.toLocaleDateString(undefined, { weekday: "short" });
        dayLabel.append(weekday, " " + date.getDate() + ordinal(date.getDate()));
        loadSlots();
    }

    function ordinal(day) {
        if (day % 100 >= 11 && day % 100 <= 13) {
            return "th";
        }
        return ["th", "st", "nd", "rd"][day % 10] || "th";
    }

    async function loadSlots() {
        if (!dateInput.value) {
            return;
        }

        const request = ++slotRequest;
        slotCount.textContent = "0";
        timeList.replaceChildren(message("Loading times…"));
        let slots;
        try {
            const response = await fetch("?handler=Slots&date=" + dateInput.value + "&guests=" + guestsInput.value);
            if (!response.ok) {
                throw new Error("HTTP " + response.status);
            }
            slots = await response.json();
        } catch (error) {
            console.error("Could not load times:", error);
            if (request === slotRequest) {
                timeList.replaceChildren(message("Couldn't load times. Please try again."));
            }
            return;
        }
        if (request !== slotRequest) {
            return;
        }

        slotCount.textContent = slots.filter(function (slot) { return slot.available; }).length;
        if (slots.length === 0) {
            timeList.replaceChildren(message("We're closed on this day."));
        } else if (!slots.some(function (slot) { return slot.available; })) {
            timeList.replaceChildren(message("No times left for " + guestsInput.value + " guests on this day."));
        } else {
            timeList.replaceChildren();
            slots.forEach(function (slot) {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "time-slot";
                button.dataset.time = slot.time;
                button.disabled = !slot.available;
                button.classList.toggle("selected", slot.time === timeInput.value);
                button.setAttribute("aria-pressed", slot.time === timeInput.value ? "true" : "false");
                button.innerHTML = '<span class="slot-dot" aria-hidden="true"></span>';
                button.append(formatTime(slot.time));
                button.addEventListener("click", function () {
                    timeInput.value = slot.time;
                    timeList.querySelectorAll(".time-slot").forEach(function (other) {
                        other.classList.toggle("selected", other === button);
                        other.setAttribute("aria-pressed", other === button ? "true" : "false");
                    });
                    updateSummary();
                });
                timeList.appendChild(button);
            });
            // A previously picked time that is no longer free is dropped.
            if (timeInput.value && !slots.some(function (slot) { return slot.time === timeInput.value && slot.available; })) {
                timeInput.value = "";
            }
        }
        updateSummary();
    }

    function message(text) {
        const note = document.createElement("p");
        note.className = "time-message";
        note.textContent = text;
        return note;
    }

    function updateSummary() {
        const ready = dateInput.value && timeInput.value;
        bookButton.disabled = !ready;
        const guests = Number(guestsInput.value);
        dateValue.textContent = dateInput.value
            ? parseDate(dateInput.value).toLocaleDateString(undefined, { weekday: "short", day: "numeric", month: "short" })
            : "—";
        timeValue.textContent = timeInput.value ? formatTime(timeInput.value) : "—";
        summary.textContent = ready
            ? guests + (guests === 1 ? " guest" : " guests") + " · "
                + parseDate(dateInput.value).toLocaleDateString(undefined, { weekday: "short", day: "numeric", month: "short" })
                + " · " + formatTime(timeInput.value)
            : "Choose a date and time.";
    }

    function setGuests(value) {
        guestsInput.value = Math.min(maxGuests, Math.max(1, value || 1));
        loadSlots();
    }

    document.getElementById("guestsDown").addEventListener("click", function () {
        setGuests(Number(guestsInput.value) - 1);
    });
    document.getElementById("guestsUp").addEventListener("click", function () {
        setGuests(Number(guestsInput.value) + 1);
    });
    guestsInput.addEventListener("change", function () {
        setGuests(Number(guestsInput.value));
    });

    prevMonth.addEventListener("click", function () {
        shownMonth = new Date(shownMonth.getFullYear(), shownMonth.getMonth() - 1, 1);
        renderCalendar();
    });
    nextMonth.addEventListener("click", function () {
        shownMonth = new Date(shownMonth.getFullYear(), shownMonth.getMonth() + 1, 1);
        renderCalendar();
    });

    document.querySelectorAll("[data-clock]").forEach(function (button) {
        button.addEventListener("click", function () {
            clock = Number(button.dataset.clock);
            document.querySelectorAll("[data-clock]").forEach(function (other) {
                other.classList.toggle("active", other === button);
            });
            timeList.querySelectorAll(".time-slot").forEach(function (slot) {
                slot.lastChild.textContent = formatTime(slot.dataset.time);
            });
            updateSummary();
        });
    });

    // Start on the date sent back after a failed submit, else the first day that can be booked.
    let start = dateInput.value ? parseDate(dateInput.value) : new Date(firstDay);
    while (!dateInput.value && !isBookable(start) && start <= lastDay) {
        start.setDate(start.getDate() + 1);
    }
    shownMonth = new Date(start.getFullYear(), start.getMonth(), 1);
    if (isBookable(start)) {
        selectDate(formatDate(start));
    } else {
        renderCalendar();
    }

    document.querySelectorAll(".cancel-booking-form").forEach(function (cancelForm) {
        AppDialog.guardSubmit(cancelForm, {
            title: "Cancel this booking?",
            message: "If you paid a deposit, it goes back to your card (usually 5–10 working days).",
            confirmText: "Cancel booking",
            cancelText: "Keep booking",
            danger: true,
            icon: "fa-calendar-xmark"
        });
    });
});

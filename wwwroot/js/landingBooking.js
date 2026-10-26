// Home page "Book a table" card (see Pages/Index.cshtml).
// Date: a calendar dropdown like the admin dashboard's, limited to the dates the booking page accepts.
// Time: the booking times of the chosen day, grouped into Morning / Afternoon / Evening (GET ?handler=Slots).
// Guests: minus and plus buttons. The form opens /Customer/Reserve with the choices filled in.
(() => {
    const form = document.getElementById("bookingCard");
    if (!form) {
        return;
    }

    const dateInput = document.getElementById("bookingDate");
    const dateButton = document.getElementById("bookingDateButton");
    const dateText = document.getElementById("bookingDateText");
    const calendar = document.getElementById("bookingCalendar");
    const timeSelect = document.getElementById("bookingTime");
    const guestsInput = document.getElementById("bookingGuests");

    const WEEKDAYS = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];
    const toIso = date =>
        `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
    const fromIso = iso => new Date(`${iso}T00:00:00`);

    const firstDay = fromIso(form.dataset.firstDay);
    const lastDay = fromIso(form.dataset.lastDay);
    const closedDays = (form.dataset.closedDays || "").split(" ").filter(Boolean).map(Number);
    const isBookable = date => date >= firstDay && date <= lastDay && !closedDays.includes(date.getDay());

    let shownMonth = new Date(firstDay.getFullYear(), firstDay.getMonth(), 1);

    // ---- Date ----
    const renderCalendar = () => {
        calendar.replaceChildren();

        const head = document.createElement("div");
        head.className = "calendar-head";
        const previous = document.createElement("button");
        previous.type = "button";
        previous.className = "calendar-nav";
        previous.setAttribute("aria-label", "Previous month");
        previous.innerHTML = '<i class="fa-solid fa-chevron-left" aria-hidden="true"></i>';
        previous.disabled = shownMonth <= new Date(firstDay.getFullYear(), firstDay.getMonth(), 1);
        previous.addEventListener("click", () => {
            shownMonth = new Date(shownMonth.getFullYear(), shownMonth.getMonth() - 1, 1);
            renderCalendar();
        });
        const title = document.createElement("strong");
        title.textContent = shownMonth.toLocaleDateString("en-MY", { month: "long", year: "numeric" });
        const next = document.createElement("button");
        next.type = "button";
        next.className = "calendar-nav";
        next.setAttribute("aria-label", "Next month");
        next.innerHTML = '<i class="fa-solid fa-chevron-right" aria-hidden="true"></i>';
        next.disabled = new Date(shownMonth.getFullYear(), shownMonth.getMonth() + 1, 1) > lastDay;
        next.addEventListener("click", () => {
            shownMonth = new Date(shownMonth.getFullYear(), shownMonth.getMonth() + 1, 1);
            renderCalendar();
        });
        head.append(previous, title, next);

        const grid = document.createElement("div");
        grid.className = "calendar-grid";
        WEEKDAYS.forEach(day => {
            const name = document.createElement("span");
            name.className = "calendar-weekday";
            name.textContent = day;
            grid.appendChild(name);
        });
        for (let i = 0; i < shownMonth.getDay(); i++) {
            grid.appendChild(document.createElement("span"));
        }

        const daysInMonth = new Date(shownMonth.getFullYear(), shownMonth.getMonth() + 1, 0).getDate();
        for (let day = 1; day <= daysInMonth; day++) {
            const date = new Date(shownMonth.getFullYear(), shownMonth.getMonth(), day);
            const iso = toIso(date);
            const button = document.createElement("button");
            button.type = "button";
            button.className = "calendar-day";
            button.textContent = day;
            button.disabled = !isBookable(date);
            button.classList.toggle("selected", iso === dateInput.value);
            button.setAttribute("aria-label", date.toLocaleDateString("en-MY", { weekday: "long", day: "numeric", month: "long" }));
            button.setAttribute("aria-pressed", iso === dateInput.value ? "true" : "false");
            button.addEventListener("click", () => chooseDate(iso));
            grid.appendChild(button);
        }

        calendar.append(head, grid);
    };

    const chooseDate = iso => {
        dateInput.value = iso;
        dateText.textContent = fromIso(iso).toLocaleDateString("en-MY", { weekday: "short", day: "numeric", month: "short", year: "numeric" });
        dateButton.classList.add("chosen");
        renderCalendar();
        bootstrap.Dropdown.getOrCreateInstance(dateButton).hide();
        loadTimes(iso);
    };

    // ---- Time ----
    let request = 0;
    const loadTimes = async iso => {
        const current = ++request;
        const previous = timeSelect.value;
        timeSelect.disabled = true;
        timeSelect.replaceChildren(new Option("Loading times…", ""));

        let sections;
        try {
            const response = await fetch(`?handler=Slots&date=${iso}`);
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }
            sections = await response.json();
        } catch (error) {
            console.error("Could not load booking times:", error);
            if (current === request) {
                timeSelect.replaceChildren(new Option("Couldn't load times. Pick the date again.", ""));
            }
            return;
        }
        if (current !== request) {
            return;
        }

        if (sections.length === 0) {
            timeSelect.replaceChildren(new Option("We're closed on this day", ""));
            return;
        }

        timeSelect.replaceChildren(new Option("Choose a time", ""));
        sections.forEach(section => {
            const group = document.createElement("optgroup");
            group.label = section.section;
            section.times.forEach(time => group.appendChild(new Option(time.label, time.value)));
            timeSelect.appendChild(group);
        });
        timeSelect.value = Array.from(timeSelect.options).some(option => option.value === previous) ? previous : "";
        timeSelect.disabled = false;
    };

    // ---- Guests ----
    const setGuests = value => {
        const max = Number(guestsInput.max) || 20;
        guestsInput.value = Math.min(max, Math.max(1, Number(value) || 1));
        document.getElementById("bookingGuestsDown").disabled = Number(guestsInput.value) <= 1;
        document.getElementById("bookingGuestsUp").disabled = Number(guestsInput.value) >= max;
    };
    document.getElementById("bookingGuestsDown").addEventListener("click", () => setGuests(Number(guestsInput.value) - 1));
    document.getElementById("bookingGuestsUp").addEventListener("click", () => setGuests(Number(guestsInput.value) + 1));
    guestsInput.addEventListener("change", () => setGuests(guestsInput.value));

    // Only the filled-in choices go to the booking page, so its address stays tidy.
    form.addEventListener("submit", () => {
        [dateInput, timeSelect].forEach(input => {
            input.disabled = !input.value;
        });
    });
    // Coming back with the Back button: make the fields usable again.
    window.addEventListener("pageshow", () => {
        dateInput.disabled = false;
        timeSelect.disabled = !dateInput.value;
    });

    setGuests(guestsInput.value);
    renderCalendar();
})();

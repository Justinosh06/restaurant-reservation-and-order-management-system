document.addEventListener("DOMContentLoaded", function () {
    const app = document.querySelector(".service-app");
    const panel = document.getElementById("tableDetailsPanel");
    const backdrop = document.getElementById("panelBackdrop");
    const closeButton = document.getElementById("closePanelButton");
    const manageLink = document.getElementById("manageTableReservations");
    const reservationItems = Array.from(
        document.querySelectorAll(".table-reservation")
    );
    const noReservations = document.getElementById("noTableReservations");
    const reservationCount = document.getElementById("tableReservationCount");
    const removeForm = document.getElementById("removeTableForm");
    const removeTableId = document.getElementById("removeTableId");
    const addPanel = document.getElementById("addTablePanel");
    const addButton = document.getElementById("addTableButton");
    const closeAddButton = document.getElementById("closeAddTableButton");

    if (!app || !panel || !backdrop || !closeButton || !manageLink
        || !removeForm || !addPanel || !addButton || !closeAddButton) {
        console.error("Table management page is missing required elements.");
        return;
    }

    function openTable(id) {
        const table = document.querySelector(
            '.floor-table[data-table-id="' + CSS.escape(id) + '"]'
        );
        if (!table) {
            console.error("Cannot open unknown table:", id);
            return;
        }

        document.getElementById("panelTitle").textContent = "Table " + id;
        document.getElementById("panelCapacity").textContent =
            table.dataset.capacity + " seats";
        manageLink.href =
            "/Admin/Reservations?tableId=" + encodeURIComponent(id);
        removeTableId.value = id;
        removeTableId.dataset.unseatCount = table.dataset.unseatCount || "0";
        removeTableId.dataset.sharedCount = table.dataset.sharedCount || "0";
        removeTableId.dataset.seatedNow = table.dataset.seatedNow || "false";

        let visibleCount = 0;
        reservationItems.forEach(function (item) {
            const visible = item.dataset.tableReservation.split(" ").includes(id);
            item.hidden = !visible;
            if (visible) {
                visibleCount++;
            }
        });

        reservationCount.textContent =
            visibleCount + (visibleCount === 1 ? " booking" : " bookings");
        noReservations.hidden = visibleCount !== 0;

        panel.classList.add("open");
        backdrop.classList.add("open");
        panel.setAttribute("aria-hidden", "false");
        closeButton.focus();
    }

    function openAddPanel() {
        addPanel.classList.add("open");
        backdrop.classList.add("open");
        addPanel.setAttribute("aria-hidden", "false");
        const checked = addPanel.querySelector("input[name=seats]:checked");
        if (checked) {
            checked.focus();
        }
    }

    // Closes whichever side panel is open.
    function closePanel() {
        [panel, addPanel].forEach(function (item) {
            item.classList.remove("open");
            item.setAttribute("aria-hidden", "true");
        });
        backdrop.classList.remove("open");
    }

    // Hovering a booking in the sidebar lights up its tables on the floor plan.
    const floorTables = Array.from(document.querySelectorAll(".floor-table"));
    document.querySelectorAll(".checkin-item").forEach(function (item) {
        const ids = item.dataset.tables ? item.dataset.tables.split(" ") : [];
        const spotlight = function (on) {
            floorTables.forEach(function (table) {
                table.classList.toggle("spotlight", on && ids.includes(table.dataset.tableId));
            });
        };
        item.addEventListener("mouseenter", function () { spotlight(true); });
        item.addEventListener("mouseleave", function () { spotlight(false); });
    });

    document.querySelectorAll(".undo-checkin-form").forEach(function (form) {
        form.addEventListener("submit", function (event) {
            if (!window.confirm("Undo this check-in?")) {
                event.preventDefault();
            }
        });
    });

    floorTables.forEach(function (button) {
        button.addEventListener("click", function () {
            openTable(button.dataset.tableId);
        });
    });

    closeButton.addEventListener("click", closePanel);
    closeAddButton.addEventListener("click", closePanel);
    addButton.addEventListener("click", openAddPanel);
    backdrop.addEventListener("click", closePanel);
    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape" && backdrop.classList.contains("open")) {
            closePanel();
        }
    });

    removeForm.addEventListener("submit", function (event) {
        if (removeTableId.dataset.seatedNow === "true") {
            event.preventDefault();
            window.alert("Guests are checked in at " + removeTableId.value
                + " right now. Remove the table once they have left.");
            return;
        }

        // Bookings are never deleted with a table; they only lose it.
        const unseat = Number(removeTableId.dataset.unseatCount);
        const shared = Number(removeTableId.dataset.sharedCount);
        let warning = "";
        if (unseat > 0) {
            warning += "\n\n" + unseat + (unseat === 1 ? " upcoming booking will go" : " upcoming bookings will go")
                + " back to Pending so you can seat " + (unseat === 1 ? "it" : "them") + " elsewhere.";
        }
        if (shared > 0) {
            warning += "\n\n" + shared + (shared === 1 ? " booking also uses" : " bookings also use")
                + " other tables and will keep them.";
        }
        if (!window.confirm("Remove table " + removeTableId.value + " from the floor plan?" + warning)) {
            event.preventDefault();
        }
    });

    if (app.dataset.selectedTable) {
        openTable(app.dataset.selectedTable);
    }
});

// Seated timers: count up from each check-in time, once a second (H:MM:SS, or M:SS under an hour).
(function () {
    const timers = Array.from(document.querySelectorAll(".seated-timer[data-since]"));
    if (timers.length === 0) {
        return;
    }

    function format(totalSeconds) {
        const seconds = Math.max(0, Math.floor(totalSeconds));
        const h = Math.floor(seconds / 3600);
        const m = Math.floor((seconds % 3600) / 60);
        const s = String(seconds % 60).padStart(2, "0");
        return h > 0 ? h + ":" + String(m).padStart(2, "0") + ":" + s : m + ":" + s;
    }

    // Matches ReservationSummary.SlotLength: after this the table is overdue (timer turns orange).
    const slotSeconds = 2 * 60 * 60;

    function tick() {
        const now = Date.now();
        timers.forEach(function (timer) {
            const since = Date.parse(timer.dataset.since);
            if (!Number.isNaN(since)) {
                const seconds = (now - since) / 1000;
                timer.textContent = format(seconds);
                timer.classList.toggle("overdue", seconds >= slotSeconds);
                timer.title = seconds >= slotSeconds ? "Over the 2-hour booking time" : "Time since check-in";
            }
        });
    }

    tick();
    setInterval(tick, 1000);
})();

// Check out: confirm before freeing the table.
document.querySelectorAll(".checkout-form").forEach(function (form) {
    form.addEventListener("submit", function (event) {
        if (!window.confirm("Check out " + form.dataset.name + "? Their table becomes free.")) {
            event.preventDefault();
        }
    });
});

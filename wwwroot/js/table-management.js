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
            "/Admin/Reservation/Management?tableId=" + encodeURIComponent(id);
        removeTableId.value = id;

        let visibleCount = 0;
        reservationItems.forEach(function (item) {
            const visible = item.dataset.tableReservation === id;
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

    document.querySelectorAll(".floor-table").forEach(function (button) {
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
        if (!window.confirm("Remove table " + removeTableId.value + " from the floor plan?")) {
            event.preventDefault();
        }
    });

    if (app.dataset.selectedTable) {
        openTable(app.dataset.selectedTable);
    }
});

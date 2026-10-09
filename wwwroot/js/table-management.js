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

    if (!app || !panel || !backdrop || !closeButton || !manageLink) {
        console.error("Table management page is missing required elements.");
        return;
    }

    function openTable(id) {
        const table = document.querySelector(
            '.floor-table[data-table-id="' + id + '"]'
        );
        if (!table) {
            console.error("Cannot open unknown table:", id);
            return;
        }

        document.getElementById("panelTitle").textContent = "Table " + id;
        document.getElementById("panelCapacity").textContent =
            table.dataset.capacity + " seats";
        manageLink.href = "/Reservation/Management?tableId=" + id;

        let visibleCount = 0;
        reservationItems.forEach(function (item) {
            const visible = Number(item.dataset.tableReservation) === Number(id);
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

    function closePanel() {
        panel.classList.remove("open");
        backdrop.classList.remove("open");
        panel.setAttribute("aria-hidden", "true");
    }

    document.querySelectorAll(".floor-table").forEach(function (button) {
        button.addEventListener("click", function () {
            openTable(button.dataset.tableId);
        });
    });

    closeButton.addEventListener("click", closePanel);
    backdrop.addEventListener("click", closePanel);
    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape" && panel.classList.contains("open")) {
            closePanel();
        }
    });

    if (app.dataset.selectedTable) {
        openTable(app.dataset.selectedTable);
    }
});

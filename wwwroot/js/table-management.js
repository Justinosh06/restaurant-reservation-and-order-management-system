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
        removeTableId.dataset.reservationCount = table.dataset.reservationCount || "0";

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

    // Assigning: pick a reservation in the sidebar, then tap a table.
    const assignBanner = document.getElementById("assignBanner");
    const assignText = document.getElementById("assignText");
    const unassignButton = document.getElementById("unassignButton");
    const cancelAssignButton = document.getElementById("cancelAssignButton");
    const assignForm = document.getElementById("assignForm");
    const floorTables = Array.from(document.querySelectorAll(".floor-table"));
    const queueItems = Array.from(document.querySelectorAll(".queue-item"));
    let selected = null;

    function describe(item) {
        return item.dataset.name + " · " + item.dataset.party + " guests · " + item.dataset.time;
    }

    function selectReservation(item) {
        selected = item;
        queueItems.forEach(function (other) {
            other.classList.toggle("selected", other === item);
            other.setAttribute("aria-pressed", other === item ? "true" : "false");
        });

        const fits = item.dataset.fits ? item.dataset.fits.split(" ") : [];
        floorTables.forEach(function (table) {
            const id = table.dataset.tableId;
            table.classList.toggle("current", id === item.dataset.table);
            table.classList.toggle("fits", fits.includes(id));
            table.classList.toggle("no-fit", id !== item.dataset.table && !fits.includes(id));
        });

        app.classList.add("assigning");
        assignBanner.hidden = false;
        assignBanner.classList.remove("warning");
        assignText.textContent = fits.length > 0
            ? "Choose a table for " + describe(item)
            : "No free table fits " + describe(item) + ". Add a bigger table or free one up.";
        unassignButton.hidden = !item.dataset.table;
    }

    function clearSelection() {
        selected = null;
        queueItems.forEach(function (item) {
            item.classList.remove("selected");
            item.setAttribute("aria-pressed", "false");
        });
        floorTables.forEach(function (table) {
            table.classList.remove("current", "fits", "no-fit");
        });
        app.classList.remove("assigning");
        assignBanner.hidden = true;
    }

    function submitAssignment(tableId) {
        document.getElementById("assignReservationId").value = selected.dataset.reservationId;
        document.getElementById("assignTableId").value = tableId;
        assignForm.submit();
    }

    queueItems.forEach(function (item) {
        item.setAttribute("aria-pressed", "false");
        item.addEventListener("click", function () {
            if (selected === item) {
                clearSelection();
            } else {
                selectReservation(item);
            }
        });
    });

    floorTables.forEach(function (button) {
        button.addEventListener("click", function () {
            if (!selected) {
                openTable(button.dataset.tableId);
                return;
            }

            const id = button.dataset.tableId;
            if (button.classList.contains("fits")) {
                submitAssignment(id);
            } else if (id === selected.dataset.table) {
                assignBanner.classList.add("warning");
                assignText.textContent = describe(selected) + " is already at " + id + ".";
            } else {
                const tooSmall = Number(button.dataset.capacity) < Number(selected.dataset.party);
                assignBanner.classList.add("warning");
                assignText.textContent = tooSmall
                    ? id + " only seats " + button.dataset.capacity + ". Pick a bigger table."
                    : id + " is already booked around " + selected.dataset.time + ". Pick another table.";
            }
        });
    });

    unassignButton.addEventListener("click", function () {
        if (selected) {
            submitAssignment("");
        }
    });
    cancelAssignButton.addEventListener("click", clearSelection);

    closeButton.addEventListener("click", closePanel);
    closeAddButton.addEventListener("click", closePanel);
    addButton.addEventListener("click", openAddPanel);
    backdrop.addEventListener("click", closePanel);
    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape" && backdrop.classList.contains("open")) {
            closePanel();
        } else if (event.key === "Escape" && selected) {
            clearSelection();
        }
    });

    removeForm.addEventListener("submit", function (event) {
        const count = Number(removeTableId.dataset.reservationCount);
        const warning = count > 0
            ? "\n\nThis also deletes its " + count + (count === 1 ? " reservation." : " reservations.")
            : "";
        if (!window.confirm("Remove table " + removeTableId.value + " from the floor plan?" + warning)) {
            event.preventDefault();
        }
    });

    if (app.dataset.selectedTable) {
        openTable(app.dataset.selectedTable);
    }
});

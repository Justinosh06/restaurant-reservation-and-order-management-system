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
        removeTableId.dataset.sharedCount = table.dataset.sharedCount || "0";

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

    // Assigning: pick a reservation in the sidebar, tap one or more free tables, then save.
    const assignBanner = document.getElementById("assignBanner");
    const assignText = document.getElementById("assignText");
    const saveAssignButton = document.getElementById("saveAssignButton");
    const unassignButton = document.getElementById("unassignButton");
    const cancelAssignButton = document.getElementById("cancelAssignButton");
    const assignForm = document.getElementById("assignForm");
    const assignTableIds = document.getElementById("assignTableIds");
    const floorTables = Array.from(document.querySelectorAll(".floor-table"));
    const queueItems = Array.from(document.querySelectorAll(".queue-item"));
    let selected = null;
    let picked = [];

    function idList(value) {
        return value ? value.split(" ") : [];
    }

    function describe(item) {
        return item.dataset.name + " · " + item.dataset.party + " guests · " + item.dataset.time;
    }

    function seatsOf(id) {
        const table = floorTables.find(function (button) {
            return button.dataset.tableId === id;
        });
        return table ? Number(table.dataset.capacity) : 0;
    }

    function sameTables(a, b) {
        return a.length === b.length && a.every(function (id) {
            return b.includes(id);
        });
    }

    // Redraws the floor plan and banner for the current reservation and picked tables.
    function showPicked() {
        const free = idList(selected.dataset.free);
        const current = idList(selected.dataset.tables);
        floorTables.forEach(function (table) {
            const id = table.dataset.tableId;
            const isPicked = picked.includes(id);
            table.classList.toggle("picked", isPicked);
            table.classList.toggle("fits", !isPicked && free.includes(id));
            table.classList.toggle("no-fit", !isPicked && !free.includes(id));
            table.setAttribute("aria-pressed", isPicked ? "true" : "false");
        });

        const party = Number(selected.dataset.party);
        const seats = picked.reduce(function (total, id) {
            return total + seatsOf(id);
        }, 0);
        const enough = picked.length > 0 && seats >= party;

        assignBanner.classList.remove("warning");
        if (picked.length === 0) {
            assignText.textContent = free.length > 0
                ? "Choose tables for " + describe(selected)
                : "No table is free for " + describe(selected) + ". Add a table or free one up.";
        } else {
            assignText.textContent = describe(selected) + ": " + seats + " of " + party + " seats chosen ("
                + picked.join(" + ") + ")" + (enough ? "." : ". Tap another table to add it.");
        }
        saveAssignButton.disabled = !enough || sameTables(picked, current);
        unassignButton.hidden = current.length === 0;
    }

    function selectReservation(item) {
        selected = item;
        picked = idList(item.dataset.tables);
        queueItems.forEach(function (other) {
            other.classList.toggle("selected", other === item);
            other.setAttribute("aria-pressed", other === item ? "true" : "false");
        });
        app.classList.add("assigning");
        assignBanner.hidden = false;
        showPicked();
    }

    function clearSelection() {
        selected = null;
        picked = [];
        queueItems.forEach(function (item) {
            item.classList.remove("selected");
            item.setAttribute("aria-pressed", "false");
        });
        floorTables.forEach(function (table) {
            table.classList.remove("picked", "fits", "no-fit");
            table.removeAttribute("aria-pressed");
        });
        app.classList.remove("assigning");
        assignBanner.hidden = true;
    }

    function submitAssignment(tableIds) {
        document.getElementById("assignReservationId").value = selected.dataset.reservationId;
        assignTableIds.replaceChildren();
        tableIds.forEach(function (id) {
            const input = document.createElement("input");
            input.type = "hidden";
            input.name = "tableIds";
            input.value = id;
            assignTableIds.appendChild(input);
        });
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
            if (picked.includes(id)) {
                picked = picked.filter(function (other) {
                    return other !== id;
                });
                showPicked();
            } else if (idList(selected.dataset.free).includes(id)) {
                picked.push(id);
                showPicked();
            } else {
                assignBanner.classList.add("warning");
                assignText.textContent = id + " is already booked around " + selected.dataset.time + ". Pick another table.";
            }
        });
    });

    saveAssignButton.addEventListener("click", function () {
        if (selected && picked.length > 0) {
            submitAssignment(picked);
        }
    });
    unassignButton.addEventListener("click", function () {
        if (selected) {
            submitAssignment([]);
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
        const shared = Number(removeTableId.dataset.sharedCount);
        let warning = count > 0
            ? "\n\nThis also deletes its " + count + (count === 1 ? " reservation." : " reservations.")
            : "";
        if (shared > 0) {
            warning += "\n\n" + shared + (shared === 1 ? " reservation also uses" : " reservations also use")
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

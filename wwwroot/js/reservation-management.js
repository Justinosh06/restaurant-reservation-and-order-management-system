document.addEventListener("DOMContentLoaded", function () {
    const app = document.querySelector(".reservation-management-page");
    const panel = document.getElementById("reservationEditorPanel");
    const backdrop = document.getElementById("reservationFormBackdrop");
    const closeButton = document.getElementById("closeReservationForm");

    if (!app || !panel || !backdrop || !closeButton) {
        console.error("Reservation management page is missing required elements.");
        return;
    }

    function closeEditor() {
        panel.classList.remove("open");
        backdrop.classList.remove("open");
        panel.setAttribute("aria-hidden", "true");
    }

    closeButton.addEventListener("click", closeEditor);
    backdrop.addEventListener("click", closeEditor);

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape" && panel.classList.contains("open")) {
            closeEditor();
        }
    });

    document.querySelectorAll(".delete-reservation-form").forEach(function (form) {
        form.addEventListener("submit", function (event) {
            if (!window.confirm("Delete this reservation? This cannot be undone.")) {
                event.preventDefault();
            }
        });
    });

    if (app.dataset.reopenForm === "true") {
        const form = app.dataset.formMode === "edit"
            ? document.getElementById("editReservationForm")
            : document.getElementById("createReservationForm");
        const firstInput = form.querySelector("input:not([type=hidden]), select");
        if (firstInput) {
            firstInput.focus();
        }
    }
});

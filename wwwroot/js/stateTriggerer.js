document.addEventListener("DOMContentLoaded", function () {
    const menu = document.getElementById("header-menu-dropdown");
    if (!menu) {
        return;
    }

    menu.addEventListener("click", function (event) {
        if (event.target.closest(".nav-button")) {
            return;
        }

        menu.classList.toggle("active");
    });

    menu.addEventListener("mouseenter", function () {
        menu.classList.add("active");
    });

    menu.addEventListener("mouseleave", function () {
        menu.classList.remove("active");
    });
});

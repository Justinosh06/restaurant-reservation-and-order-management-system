(() => {
    document.addEventListener("DOMContentLoaded", () => {
        const pills = document.querySelectorAll(".filter-pill");
        const items = document.querySelectorAll(".item-card-link");
        const menuTotalCount = document.getElementById("menu-total-count");

        pills.forEach(pill => {
            pill.addEventListener("click", () => {
                pills.forEach(p => p.classList.remove("active"));
                pill.classList.add("active");

                const selectedCategory = pill.getAttribute("data-category");
                let visibleCount = 0;

                items.forEach(item => {
                    const itemCategory = item.getAttribute("data-category");

                    if (selectedCategory === "all" || itemCategory === selectedCategory) {
                        item.style.display = "";
                        visibleCount++;
                    } else {
                        item.style.display = "none";
                    }
                });

                if (menuTotalCount) {
                    menuTotalCount.textContent = visibleCount;
                }
            });
        });
    });
})();

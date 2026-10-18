(() => {
    const cartBadge = document.querySelector(".content-number");
    if (cartBadge?.dataset.tableId) {
        const savedCount = localStorage.getItem(`cart-item-count-${cartBadge.dataset.tableId}`);
        if (savedCount !== null) cartBadge.textContent = savedCount;
    }
})();

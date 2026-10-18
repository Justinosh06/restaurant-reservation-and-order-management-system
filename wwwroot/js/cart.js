(() => {
    const cards = document.querySelectorAll(".cart-column .item-card");
    const tableId = document.querySelector(".content-number")?.dataset.tableId;
    const cartItemCount = document.getElementById("cart-item-count");
    const initialCount = cartItemCount.textContent;
    if (tableId) localStorage.setItem(`cart-item-count-${tableId}`, initialCount);

    cards.forEach(card => {
        const itemId = card.getAttribute("data-item-id");
        const price = parseFloat(card.getAttribute("data-price"));
        const btnDecrease = card.querySelector(".btn-decrease");
        const btnIncrease = card.querySelector(".btn-increase");
        const qtyValEl = card.querySelector(".quantity-val");

        btnIncrease.addEventListener("click", () => {
            let qty = parseInt(qtyValEl.textContent) + 1;
            qtyValEl.textContent = qty;
            updateSummary(itemId, price, qty);
        });

        btnDecrease.addEventListener("click", () => {
            let qty = parseInt(qtyValEl.textContent);
            if (qty > 1) {
                qty--;
                qtyValEl.textContent = qty;
                updateSummary(itemId, price, qty);
            }
        });
    });

    function updateSummary(itemId, unitPrice, quantity) {
        const summaryRow = document.getElementById(`summary-item-${itemId}`);
        if (summaryRow) {
            summaryRow.querySelector(".summary-qty").textContent = quantity;
            summaryRow.querySelector(".summary-price").textContent = `RM ${(unitPrice * quantity).toFixed(2)}`;
        }

        let total = 0;
        let totalItems = 0;

        document.querySelectorAll(".cart-column .item-card").forEach(card => {
            const price = parseFloat(card.getAttribute("data-price"));
            const qty = parseInt(card.querySelector(".quantity-val").textContent);
            total += price * qty;
            totalItems += qty;
        });

        document.getElementById("grand-total").textContent = `RM ${total.toFixed(2)}`;
        cartItemCount.textContent = totalItems;
        document.querySelector(".content-number").textContent = totalItems;
        if (tableId) localStorage.setItem(`cart-item-count-${tableId}`, totalItems);
    }
})();

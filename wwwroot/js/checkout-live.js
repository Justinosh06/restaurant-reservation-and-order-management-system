(() => {
    const liveStatus = document.getElementById("checkout-live-status");
    if (!liveStatus) return;

    const sessionId = liveStatus.dataset.sessionId;
    const paymentIds = JSON.parse(liveStatus.dataset.paymentIds || "[]");
    const paymentForm = document.getElementById("checkout-payment-form");
    const paymentLabel = document.getElementById("checkout-payment-label");
    const payButton = document.getElementById("checkout-pay-button");
    const updatePaymentLabel = () => {
        const selectedMethod = paymentForm?.querySelector('input[name="paymentMethod"]:checked')?.value;
        if (!paymentLabel || !selectedMethod) return;

        payButton?.classList.toggle("pay-at-counter", selectedMethod === "cash");
        paymentLabel.replaceChildren(
            document.createTextNode(selectedMethod === "cash" ? "Request counter payment" : "Pay with "),
            ...(selectedMethod === "cash"
                ? []
                : [Object.assign(document.createElement("span"), { className: "fa-brands fa-stripe icon" })])
        );
    };
    paymentForm?.addEventListener("change", updatePaymentLabel);
    updatePaymentLabel();

    const statusUrl = new URL(liveStatus.dataset.statusUrl, window.location.origin);
    statusUrl.searchParams.set("sessionId", sessionId);
    for (const paymentId of paymentIds) {
        statusUrl.searchParams.append("paymentIds", paymentId);
    }

    let stopped = false;
    let polling = false;

    const refresh = async () => {
        if (stopped || polling || !sessionId) return;
        polling = true;
        try {
            const response = await fetch(statusUrl, { cache: "no-store", headers: { Accept: "application/json" } });
            if (!response.ok) throw new Error("Order status could not be refreshed.");

            const data = await response.json();
            for (const order of data.orders) {
                const status = document.querySelector(`[data-order-status="${CSS.escape(order.id)}"]`);
                if (status) status.textContent = order.status;

                const cancelForm = status?.closest(".order-wrapper")?.querySelector(".cancel-order-form");
                if (cancelForm) cancelForm.hidden = !order.canCancel;
            }

            if (data.cashPaid && data.paymentStatusUrl) {
                stopped = true;
                liveStatus.hidden = false;
                liveStatus.textContent = "Cash payment confirmed. Opening your payment receipt…";
                window.location.assign(data.paymentStatusUrl);
                return;
            }

            liveStatus.hidden = false;
            liveStatus.textContent = data.cashPaymentPending
                ? "Order statuses are up to date. Waiting for the counter to confirm cash payment."
                : "Order statuses are up to date.";
        } catch {
            liveStatus.hidden = false;
            liveStatus.textContent = "Unable to refresh order status right now. Retrying…";
        } finally {
            polling = false;
            if (!stopped) window.setTimeout(refresh, 2500);
        }
    };

    refresh();
})();

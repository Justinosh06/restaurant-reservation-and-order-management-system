const AdminUI = (() => {
    const escapeHtml = (value) => String(value ?? "")
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");

    const formatCurrency = (amountInCents) =>
        "RM " + (amountInCents / 100).toLocaleString("en-MY", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    const formatDate = (date) =>
        new Date(date).toLocaleDateString("en-MY", { day: "numeric", month: "short", year: "numeric" });

    const formatTime = (date) =>
        new Date(date).toLocaleTimeString("en-MY", { hour: "2-digit", minute: "2-digit" });

    const STATUS_BADGES = {
        Pending: "text-bg-warning",
        Preparing: "text-bg-primary",
        Served: "text-bg-success",
        Active: "text-bg-success",
        Scheduled: "text-bg-warning",
        Ended: "text-bg-secondary",
        Expired: "text-bg-secondary",
        Hidden: "text-bg-secondary",
        Administrator: "text-bg-dark",
        Staff: "bg-body-secondary text-body border"
    };

    const statusBadge = (status) =>
        `<span class="badge ${STATUS_BADGES[status] ?? "text-bg-secondary"}">${escapeHtml(status)}</span>`;

    const tableMessageRow = (colspan, message, tone = "text-muted") =>
        `<tr><td colspan="${colspan}" class="text-center ${tone} py-4">${escapeHtml(message)}</td></tr>`;

    let toastTimer;
    const showToast = (message, tone = "dark") => {
        let toast = document.getElementById("admin-toast");
        if (!toast) {
            toast = document.createElement("div");
            toast.id = "admin-toast";
            toast.style.zIndex = 2000;
            toast.setAttribute("role", "status");
            document.body.appendChild(toast);
        }
        toast.className = `alert alert-${tone} position-fixed bottom-0 end-0 m-3 shadow`;
        toast.textContent = message;
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => toast.classList.add("d-none"), 3000);
    };

    const showError = (error) => showToast(error.message, "danger");

    const antiForgeryToken = () =>
        document.querySelector("input[name='__RequestVerificationToken']")?.value ?? "";

    const request = async (handler, { method = "GET", json, formData } = {}) => {
        const url = new URL(window.location.pathname, window.location.origin);
        url.searchParams.set("handler", handler);

        const headers = { "Accept": "application/json" };
        let body;
        if (json !== undefined) {
            headers["Content-Type"] = "application/json";
            body = JSON.stringify(json);
        } else if (formData) {
            body = formData;
        }
        if (method !== "GET") headers["RequestVerificationToken"] = antiForgeryToken();

        let response;
        try {
            response = await fetch(url, { method, headers, body });
        } catch {
            throw new Error("Can't reach the server. Check your connection and try again.");
        }

        const data = response.status === 204 ? null : await response.json().catch(() => null);
        if (!response.ok) {
            const error = new Error(data?.error ?? "Something went wrong. Please try again.");
            error.status = response.status;
            error.data = data;
            throw error;
        }
        return data;
    };

    const get = (handler) => request(handler);
    const post = (handler, json = {}) => request(handler, { method: "POST", json });
    const postForm = (handler, formData) => request(handler, { method: "POST", formData });

    const setBusy = (button, busy) => {
        button.disabled = busy;
        button.setAttribute("aria-busy", String(busy));
    };

    return {
        escapeHtml, formatCurrency, formatDate, formatTime, statusBadge, tableMessageRow,
        showToast, showError, get, post, postForm, setBusy
    };
})();

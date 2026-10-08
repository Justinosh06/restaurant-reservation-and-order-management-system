// Shared helpers for admin pages (frontend-only mock UI; no backend calls yet).
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

    const generateId = (prefix) => prefix + "-" + Math.random().toString(36).slice(2, 8).toUpperCase();

    // Bootstrap badge class for each status shown in the admin pages.
    const STATUS_BADGES = {
        Pending: "text-bg-warning",
        Preparing: "text-bg-primary",
        Served: "text-bg-success",
        Active: "text-bg-success",
        Scheduled: "text-bg-warning",
        Ended: "text-bg-secondary",
        Hidden: "text-bg-secondary",
        Administrator: "text-bg-dark",
        Staff: "text-bg-light border"
    };

    const statusBadge = (status) =>
        `<span class="badge ${STATUS_BADGES[status] ?? "text-bg-secondary"}">${escapeHtml(status)}</span>`;

    // Small popup message using a Bootstrap alert.
    let toastTimer;
    const showToast = (message) => {
        let toast = document.getElementById("admin-toast");
        if (!toast) {
            toast = document.createElement("div");
            toast.id = "admin-toast";
            toast.className = "alert alert-dark position-fixed bottom-0 end-0 m-3 shadow";
            toast.style.zIndex = 2000;
            toast.setAttribute("role", "status");
            document.body.appendChild(toast);
        }
        toast.textContent = message;
        toast.classList.remove("d-none");
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => toast.classList.add("d-none"), 2400);
    };

    return { escapeHtml, formatCurrency, formatDate, formatTime, generateId, statusBadge, showToast };
})();

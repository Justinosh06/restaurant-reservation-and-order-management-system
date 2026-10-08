// Mock data - mirrors the Admin model (Id, Email, Password, Role). Replace with API calls later.
const staffs = [
    { id: "ADM-0001", email: "manager@resto.com", role: "Administrator" },
    { id: "STF-0002", email: "aisyah.kitchen@resto.com", role: "Staff" },
    { id: "STF-0003", email: "jason.floor@resto.com", role: "Staff" },
    { id: "STF-0004", email: "priya.cashier@resto.com", role: "Staff" }
];

(() => {
    const bodyEl = document.getElementById("staff-body");
    const emptyEl = document.getElementById("staff-empty");
    const countEl = document.getElementById("staff-count");
    const searchEl = document.getElementById("staff-search");

    const form = document.getElementById("staff-form");
    const idInput = document.getElementById("staff-id");
    const emailInput = document.getElementById("staff-email");
    const passwordInput = document.getElementById("staff-password");
    const roleInput = document.getElementById("staff-role");
    const staffModal = new bootstrap.Modal(document.getElementById("staff-modal"));
    const deleteModal = new bootstrap.Modal(document.getElementById("delete-modal"));
    let pendingDeleteId = null;

    const render = () => {
        const term = searchEl.value.trim().toLowerCase();
        const visible = staffs.filter(s => !term || s.email.toLowerCase().includes(term));

        countEl.textContent = `${staffs.length} account${staffs.length === 1 ? "" : "s"}`;
        emptyEl.classList.toggle("d-none", visible.length > 0);

        bodyEl.innerHTML = visible.map(s => `
            <tr>
                <td class="fw-medium">${AdminUI.escapeHtml(s.id)}</td>
                <td class="text-truncate" title="${AdminUI.escapeHtml(s.email)}">${AdminUI.escapeHtml(s.email)}</td>
                <td>${AdminUI.statusBadge(s.role)}</td>
                <td class="text-end text-nowrap">
                    <button type="button" class="btn btn-sm btn-outline-secondary btn-icon" data-edit="${s.id}" aria-label="Edit ${AdminUI.escapeHtml(s.email)}"><i class="fa-solid fa-pen"></i></button>
                    <button type="button" class="btn btn-sm btn-outline-danger btn-icon ms-1" data-delete="${s.id}" aria-label="Delete ${AdminUI.escapeHtml(s.email)}"><i class="fa-solid fa-trash"></i></button>
                </td>
            </tr>`).join("");
    };

    const openForm = (staff) => {
        form.classList.remove("was-validated");
        [emailInput, passwordInput].forEach(i => i.setCustomValidity(""));
        idInput.value = staff?.id ?? "";
        emailInput.value = staff?.email ?? "";
        passwordInput.value = "";
        passwordInput.required = !staff;
        roleInput.value = staff?.role ?? "Staff";

        document.getElementById("staff-modal-title").textContent = staff ? "Edit staff" : "Add staff";
        document.getElementById("staff-submit").textContent = staff ? "Save changes" : "Create account";
        document.getElementById("password-help").textContent = staff
            ? "Leave blank to keep the current password."
            : "At least 8 characters. Share it with the staff member so they can sign in.";
        staffModal.show();
    };

    document.getElementById("btn-add-staff").addEventListener("click", () => openForm(null));

    document.getElementById("toggle-password").addEventListener("click", (e) => {
        const show = passwordInput.type === "password";
        passwordInput.type = show ? "text" : "password";
        e.currentTarget.innerHTML = `<i class="fa-regular ${show ? "fa-eye-slash" : "fa-eye"}"></i>`;
    });

    form.addEventListener("submit", (e) => {
        e.preventDefault();
        const editingId = idInput.value;
        const email = emailInput.value.trim().toLowerCase();

        const duplicate = staffs.some(s => s.email === email && s.id !== editingId);
        emailInput.setCustomValidity(duplicate ? "duplicate" : "");
        const passwordTooShort = passwordInput.value.length > 0 && passwordInput.value.length < 8;
        passwordInput.setCustomValidity(passwordTooShort ? "too short" : "");

        form.classList.add("was-validated");
        if (!form.checkValidity()) return;

        if (editingId) {
            const staff = staffs.find(s => s.id === editingId);
            staff.email = email;
            staff.role = roleInput.value;
            AdminUI.showToast("Staff account updated");
        } else {
            const prefix = roleInput.value === "Administrator" ? "ADM" : "STF";
            staffs.push({ id: AdminUI.generateId(prefix), email, role: roleInput.value });
            AdminUI.showToast("Staff account created");
        }

        staffModal.hide();
        render();
    });

    bodyEl.addEventListener("click", (e) => {
        const editBtn = e.target.closest("[data-edit]");
        if (editBtn) {
            openForm(staffs.find(s => s.id === editBtn.dataset.edit));
            return;
        }

        const deleteBtn = e.target.closest("[data-delete]");
        if (deleteBtn) {
            pendingDeleteId = deleteBtn.dataset.delete;
            document.getElementById("delete-email").textContent = staffs.find(s => s.id === pendingDeleteId).email;
            deleteModal.show();
        }
    });

    document.getElementById("confirm-delete").addEventListener("click", () => {
        const index = staffs.findIndex(s => s.id === pendingDeleteId);
        if (index >= 0) staffs.splice(index, 1);
        pendingDeleteId = null;
        deleteModal.hide();
        AdminUI.showToast("Staff account deleted");
        render();
    });

    searchEl.addEventListener("input", render);

    render();
})();

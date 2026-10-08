(() => {
    const COLUMN_COUNT = 4;

    let staffs = [];
    let pendingDeleteId = null;

    const bodyEl = document.getElementById("staff-body");
    const countEl = document.getElementById("staff-count");
    const searchEl = document.getElementById("staff-search");

    const form = document.getElementById("staff-form");
    const formError = document.getElementById("staff-form-error");
    const submitBtn = document.getElementById("staff-submit");
    const idInput = document.getElementById("staff-id");
    const emailInput = document.getElementById("staff-email");
    const passwordInput = document.getElementById("staff-password");
    const roleInput = document.getElementById("staff-role");
    const confirmDeleteBtn = document.getElementById("confirm-delete");
    const staffModal = new bootstrap.Modal(document.getElementById("staff-modal"));
    const deleteModal = new bootstrap.Modal(document.getElementById("delete-modal"));

    const render = () => {
        countEl.textContent = `${staffs.length} account${staffs.length === 1 ? "" : "s"}`;

        if (staffs.length === 0) {
            bodyEl.innerHTML = AdminUI.tableMessageRow(COLUMN_COUNT, "No staff accounts yet. Use \"Add staff\" to create the first one.");
            return;
        }

        const term = searchEl.value.trim().toLowerCase();
        const visible = staffs.filter(s => !term || s.email.includes(term));
        if (visible.length === 0) {
            bodyEl.innerHTML = AdminUI.tableMessageRow(COLUMN_COUNT, "No staff accounts match this search.");
            return;
        }

        bodyEl.innerHTML = visible.map(s => `
            <tr>
                <td class="fw-medium">${AdminUI.escapeHtml(s.displayId)}</td>
                <td class="text-truncate" title="${AdminUI.escapeHtml(s.email)}">${AdminUI.escapeHtml(s.email)}</td>
                <td>${AdminUI.statusBadge(s.role)}</td>
                <td class="text-end text-nowrap">
                    <button type="button" class="btn btn-sm btn-outline-secondary btn-icon" data-edit="${s.id}" aria-label="Edit ${AdminUI.escapeHtml(s.email)}"><i class="fa-solid fa-pen"></i></button>
                    <button type="button" class="btn btn-sm btn-outline-danger btn-icon ms-1" data-delete="${s.id}" aria-label="Delete ${AdminUI.escapeHtml(s.email)}"><i class="fa-solid fa-trash"></i></button>
                </td>
            </tr>`).join("");
    };

    const load = () => {
        bodyEl.innerHTML = AdminUI.tableMessageRow(COLUMN_COUNT, "Loading...");
        AdminUI.get("List")
            .then(data => { staffs = data; render(); })
            .catch(error => {
                bodyEl.innerHTML = AdminUI.tableMessageRow(COLUMN_COUNT, error.message, "text-danger");
                AdminUI.showError(error);
            });
    };

    const openForm = (staff) => {
        form.classList.remove("was-validated");
        formError.classList.add("d-none");
        idInput.value = staff?.id ?? "";
        emailInput.value = staff?.email ?? "";
        passwordInput.value = "";
        passwordInput.required = !staff;
        roleInput.value = staff?.role ?? "Staff";

        document.getElementById("staff-modal-title").textContent = staff ? "Edit staff" : "Add staff";
        submitBtn.textContent = staff ? "Save changes" : "Create account";
        document.getElementById("password-help").textContent = staff
            ? "Leave blank to keep the current password."
            : `At least ${passwordInput.minLength} characters. Share it with the staff member so they can sign in.`;
        staffModal.show();
    };

    document.getElementById("btn-add-staff").addEventListener("click", () => openForm(null));

    document.getElementById("toggle-password").addEventListener("click", (e) => {
        const show = passwordInput.type === "password";
        passwordInput.type = show ? "text" : "password";
        e.currentTarget.innerHTML = `<i class="fa-regular ${show ? "fa-eye-slash" : "fa-eye"}"></i>`;
    });

    form.addEventListener("submit", async (e) => {
        e.preventDefault();
        formError.classList.add("d-none");
        form.classList.add("was-validated");
        if (!form.checkValidity()) return;

        const editingId = idInput.value;
        const payload = {
            id: editingId || null,
            email: emailInput.value.trim(),
            password: passwordInput.value || null,
            role: roleInput.value
        };

        AdminUI.setBusy(submitBtn, true);
        try {
            const saved = await AdminUI.post(editingId ? "Update" : "Create", payload);
            const index = staffs.findIndex(s => s.id === saved.id);
            if (index >= 0) staffs[index] = saved; else staffs.push(saved);
            staffModal.hide();
            AdminUI.showToast(editingId ? "Staff account updated" : "Staff account created");
            render();
        } catch (error) {
            formError.textContent = error.message;
            formError.classList.remove("d-none");
        } finally {
            AdminUI.setBusy(submitBtn, false);
        }
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

    confirmDeleteBtn.addEventListener("click", async () => {
        AdminUI.setBusy(confirmDeleteBtn, true);
        try {
            await AdminUI.post("Delete", { id: pendingDeleteId });
            staffs = staffs.filter(s => s.id !== pendingDeleteId);
            AdminUI.showToast("Staff account deleted");
            render();
        } catch (error) {
            AdminUI.showError(error);
        } finally {
            pendingDeleteId = null;
            deleteModal.hide();
            AdminUI.setBusy(confirmDeleteBtn, false);
        }
    });

    searchEl.addEventListener("input", render);

    load();
})();

// Styled replacement for the browser's confirm() and alert() boxes.
//
//   const ok = await AppDialog.confirm({ title, message, confirmText, cancelText, danger, icon });
//   await AppDialog.alert({ title, message, icon });
//   AppDialog.guardSubmit(form, options)   // asks before a form submits; options may be a function
//                                          // that returns the options, or null to skip the question.
(function () {
    let open = null;

    function show(options, withCancel) {
        if (open) {
            open.finish(false);
        }

        return new Promise(function (resolve) {
            const backdrop = document.createElement("div");
            backdrop.className = "app-dialog-backdrop";
            backdrop.innerHTML =
                '<div class="app-dialog" role="alertdialog" aria-modal="true" aria-labelledby="appDialogTitle" aria-describedby="appDialogMessage">' +
                '  <span class="app-dialog-icon" aria-hidden="true"><i></i></span>' +
                '  <h2 class="app-dialog-title" id="appDialogTitle"></h2>' +
                '  <p class="app-dialog-message" id="appDialogMessage"></p>' +
                '  <div class="app-dialog-actions">' +
                '    <button type="button" class="app-dialog-cancel"></button>' +
                '    <button type="button" class="app-dialog-confirm"></button>' +
                '  </div>' +
                '</div>';

            const dialog = backdrop.querySelector(".app-dialog");
            const confirmButton = backdrop.querySelector(".app-dialog-confirm");
            const cancelButton = backdrop.querySelector(".app-dialog-cancel");
            dialog.classList.toggle("danger", Boolean(options.danger));
            backdrop.querySelector(".app-dialog-icon i").className = "fa-solid " +
                (options.icon || (options.danger ? "fa-triangle-exclamation" : withCancel ? "fa-circle-question" : "fa-circle-info"));
            backdrop.querySelector(".app-dialog-title").textContent = options.title || "";
            backdrop.querySelector(".app-dialog-message").textContent = options.message || "";
            backdrop.querySelector(".app-dialog-message").hidden = !options.message;
            confirmButton.textContent = options.confirmText || "OK";
            cancelButton.textContent = options.cancelText || "Cancel";
            cancelButton.hidden = !withCancel;

            const previousFocus = document.activeElement;
            function finish(result) {
                document.removeEventListener("keydown", onKey, true);
                backdrop.classList.remove("open");
                setTimeout(function () { backdrop.remove(); }, 150);
                open = null;
                if (previousFocus && previousFocus.focus) {
                    previousFocus.focus();
                }
                resolve(result);
            }
            function onKey(event) {
                if (event.key === "Escape") {
                    event.preventDefault();
                    event.stopPropagation();
                    finish(false);
                }
            }

            confirmButton.addEventListener("click", function () { finish(true); });
            cancelButton.addEventListener("click", function () { finish(false); });
            backdrop.addEventListener("click", function (event) {
                if (event.target === backdrop) {
                    finish(false);
                }
            });
            document.addEventListener("keydown", onKey, true);

            document.body.appendChild(backdrop);
            open = { finish: finish };
            requestAnimationFrame(function () { backdrop.classList.add("open"); });
            confirmButton.focus();
        });
    }

    window.AppDialog = {
        confirm: function (options) { return show(options || {}, true); },
        alert: function (options) { return show(options || {}, false).then(function () {}); },
        guardSubmit: function (form, options) {
            form.addEventListener("submit", function (event) {
                if (form.dataset.dialogConfirmed === "true") {
                    return;
                }
                event.preventDefault();
                const resolved = typeof options === "function" ? options() : options;
                if (!resolved) {
                    return;
                }
                show(resolved, true).then(function (ok) {
                    if (ok) {
                        form.dataset.dialogConfirmed = "true";
                        form.submit();
                    }
                });
            });
        }
    };
})();

// Going Back to this page after logging in loads it again, which sends the customer to their dashboard.
window.addEventListener('pageshow', function (e) {
    if (e.persisted) {
        location.reload();
    }
});

(function () {
    const form = document.getElementById('loginForm');
    const loginButton = document.getElementById('loginButton');
    const modalElement = document.getElementById('captchaModal');
    const modal = new bootstrap.Modal(modalElement);
    const image = document.getElementById('captchaImage');
    const answer = document.getElementById('Input_CaptchaAnswer');
    const error = document.getElementById('captchaError');

    function loadCaptcha() {
        answer.value = '';
        image.src = '?handler=Captcha&t=' + Date.now();
    }

    loginButton.addEventListener('click', function () {
        if (!$(form).valid()) {
            return;
        }
        error.classList.add('d-none');
        loadCaptcha();
        modal.show();
    });

    modalElement.addEventListener('shown.bs.modal', () => answer.focus());
    document.getElementById('refreshCaptcha').addEventListener('click', loadCaptcha);

    form.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !modalElement.contains(e.target)) {
            e.preventDefault();
            loginButton.click();
        }
    });

    form.addEventListener('submit', function (e) {
        if (!answer.value.trim()) {
            e.preventDefault();
            error.classList.remove('d-none');
        }
    });
})();

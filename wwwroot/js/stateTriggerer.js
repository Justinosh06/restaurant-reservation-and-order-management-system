const HeaderMenuDropdown = document.getElementById("header-menu-dropdown");

// The sidebar admin layout has no header dropdown, so only wire it up when present.
if (HeaderMenuDropdown) {
    const events = ['mouseover', 'click'];

    events.forEach((event) => {
        HeaderMenuDropdown.addEventListener(event, () => {
            HeaderMenuDropdown.classList.toggle("active")
        })
    })

HeaderMenuDropdown.addEventListener('mouseout', () => {
    HeaderMenuDropdown.classList.remove("active")
})
const HeaderMenuDropdown = document.getElementById("header-menu-dropdown");

// The sidebar layout has no drop-down menu, so only wire the events when it exists.
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
}

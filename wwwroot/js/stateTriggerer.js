const HeaderMenuDropdown = document.getElementById("header-menu-dropdown");

const events = ['mouseover', 'click'];

events.forEach((event) => {
    HeaderMenuDropdown.addEventListener(event, () => {
        HeaderMenuDropdown.classList.toggle("active")
    })
})

HeaderMenuDropdown.addEventListener('mouseout', () => {
    HeaderMenuDropdown.classList.remove("active")
})
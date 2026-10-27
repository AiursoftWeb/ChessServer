const url = new URL(location.href);
const now = new Date();
if (!url.searchParams.has('date') && !url.searchParams.has('offsetMinutes')) {
    url.searchParams.set('date', `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`);
    url.searchParams.set('offsetMinutes', String(now.getTimezoneOffset()));
    location.replace(url);
}
document.querySelectorAll('.history-time').forEach(element => {
    const value = element.getAttribute('datetime');
    element.textContent = new Date(value.endsWith('Z') ? value : `${value}Z`).toLocaleTimeString([], {hour: '2-digit', minute: '2-digit'});
});

document.querySelectorAll('[data-time-increment]').forEach(increment => {
    const minutes = increment.form.querySelector('[name="Minutes"], [name="minutes"]');
    function update() {
        increment.disabled = minutes.value === '0';
        if (increment.disabled) increment.value = '0';
    }
    minutes.addEventListener('change', update);
    update();
});

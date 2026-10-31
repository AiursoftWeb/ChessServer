const difficulty = document.getElementById('difficulty');
const field = document.getElementById('llm-model-field');
const model = document.getElementById('llm-model');
function updateModel() {
    if (!field || !model) return;
    const enabled = difficulty?.value === 'llm';
    field.hidden = !enabled;
    model.disabled = !enabled;
}
difficulty?.addEventListener('change', updateModel);
window.addEventListener('pageshow', updateModel);
updateModel();

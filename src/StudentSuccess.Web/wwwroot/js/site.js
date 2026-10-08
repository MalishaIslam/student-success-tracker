// Small progressive enhancements. Every page works without JavaScript.

document.addEventListener('DOMContentLoaded', () => {
  // Ask before removing a score (the server still requires a POST with an anti-forgery token)
  for (const form of document.querySelectorAll('form[action*="deletescore"]')) {
    form.addEventListener('submit', (event) => {
      if (!window.confirm('Remove this result?')) event.preventDefault();
    });
  }

  // After a form error, move keyboard focus to the error summary so it is announced
  const summary = document.querySelector('.error-summary.validation-summary-errors');
  if (summary) {
    summary.setAttribute('tabindex', '-1');
    summary.focus();
  }
});

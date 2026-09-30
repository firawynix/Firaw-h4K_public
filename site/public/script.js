const toggle = document.querySelector('#motion-toggle');
const arena = document.querySelector('.arena');
toggle?.addEventListener('click', () => {
  const paused = arena.classList.toggle('paused');
  toggle.setAttribute('aria-pressed', String(paused));
  toggle.textContent = paused ? 'Retomar prévia' : 'Pausar prévia';
});

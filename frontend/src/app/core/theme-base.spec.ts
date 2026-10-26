describe('Tema: .visually-hidden', () => {
  // Fase 17: o aviso para leitor de tela no fim das Configurações ficava abaixo da janela e
  // criava uma segunda rolagem, além da rolagem da própria página.
  it('dentro de uma área que rola, não estica o documento', () => {
    // Área sem position, como o :host das páginas: o absoluto de dentro se posiciona pelo documento.
    const scroller = document.createElement('div');
    scroller.style.cssText = 'height: 100px; overflow: auto';
    scroller.innerHTML = '<div style="height: 5000px"></div>';
    document.body.appendChild(scroller);

    try {
      const before = document.documentElement.scrollHeight;
      const status = document.createElement('span');
      status.className = 'visually-hidden';
      status.textContent = 'salvo';
      scroller.appendChild(status);

      expect(document.documentElement.scrollHeight).toBe(before);
    } finally {
      scroller.remove();
    }
  });
});

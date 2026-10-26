import { Injectable, signal } from '@angular/core';

export type ThemeChoice = 'system' | 'light' | 'dark';

// Paletas do design system (seletor na barra). "papel" é a padrão. As outras são propostas para o dono
// comparar (Fase 13 e pesquisa de referências); a escolha definitiva segue com ele.
export type PaletteChoice = 'papel' | 'nautica' | 'planta' | 'grafite' | 'cobalto' | 'vinho' | 'monocromo' | 'rosa' | 'artico';

export const PALETTE_LABEL: Record<PaletteChoice, string> = {
  papel: 'Papel',
  nautica: 'Carta náutica',
  planta: 'Planta técnica',
  grafite: 'Grafite e índigo',
  cobalto: 'Cobalto e laranja',
  vinho: 'Vinho e pedra',
  monocromo: 'Monocromo e azul',
  rosa: 'Rosa e carvão',
  artico: 'Ártico',
};

export const PALETTES: PaletteChoice[] = ['papel', 'nautica', 'planta', 'grafite', 'cobalto', 'vinho', 'monocromo', 'rosa', 'artico'];

const THEME_KEY = 'rastro.theme';
const PALETTE_KEY = 'rastro.palette';

// Tema (claro, escuro ou do sistema) e paleta. A escolha fica no navegador (conveniência por pessoa).
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly choice = signal<ThemeChoice>(readTheme());
  readonly palette = signal<PaletteChoice>(readPalette());

  constructor() {
    this.apply();
  }

  cycle(): void {
    const next: Record<ThemeChoice, ThemeChoice> = { system: 'light', light: 'dark', dark: 'system' };
    this.set(next[this.choice()]);
  }

  set(choice: ThemeChoice): void {
    this.choice.set(choice);
    this.apply();
    store(THEME_KEY, choice);
  }

  setPalette(palette: PaletteChoice): void {
    this.palette.set(palette);
    this.apply();
    store(PALETTE_KEY, palette);
  }

  private apply(): void {
    const root = document.documentElement;
    const theme = this.choice();
    if (theme === 'system') root.removeAttribute('data-theme');
    else root.setAttribute('data-theme', theme);

    const palette = this.palette();
    if (palette === 'papel') root.removeAttribute('data-palette');
    else root.setAttribute('data-palette', palette);
  }
}

function readTheme(): ThemeChoice {
  const stored = read(THEME_KEY);
  return stored === 'light' || stored === 'dark' ? stored : 'system';
}

function readPalette(): PaletteChoice {
  const stored = read(PALETTE_KEY);
  return PALETTES.includes(stored as PaletteChoice) ? (stored as PaletteChoice) : 'papel';
}

function read(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function store(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Armazenamento bloqueado (aba privada): a escolha vale só nesta visita.
  }
}

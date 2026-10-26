import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AiStatusService } from './core/ai-status.service';
import { PALETTES, PALETTE_LABEL, PaletteChoice, ThemeChoice, ThemeService } from './core/theme.service';

const THEME_LABEL: Record<ThemeChoice, string> = {
  system: 'Tema do sistema',
  light: 'Tema claro',
  dark: 'Tema escuro',
};

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly title = 'Rastro AI';
  protected readonly theme = inject(ThemeService);
  protected readonly themeLabel = computed(() => THEME_LABEL[this.theme.choice()]);
  protected readonly ai = inject(AiStatusService);
  protected readonly palettes = PALETTES;
  protected readonly paletteLabel = PALETTE_LABEL;

  protected choosePalette(value: string): void {
    this.theme.setPalette(value as PaletteChoice);
  }

  constructor() {
    this.ai.refresh();
  }
}

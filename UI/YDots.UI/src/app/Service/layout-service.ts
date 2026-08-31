// src/app/core/services/layout.service.ts
import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class LayoutService {

  private htmlElement = document.documentElement;

  /** Tracks whether the theme customizer panel is open (double-menu beside sidebar) */
    themePanelOpen = false;

    /** Tracks whether the Theme Settings sidebar submenu (dropdown) is expanded */
    themeMenuOpen = false;

    /** Tracks which theme section is active (layout, colors, mode, etc.) */
    activeThemeSection = 'layout';

    private readonly themePanelStorageKey = 'theme-panel-open';
    private readonly themeMenuStorageKey = 'theme-menu-open';

    toggleThemePanel(): void {
      this.themePanelOpen = !this.themePanelOpen;
      this.themeMenuOpen = this.themePanelOpen;
      this.persistThemeState();
      this.syncThemePanelClass();
    }

    openThemePanel(): void {
      this.themePanelOpen = true;
      this.themeMenuOpen = true;
      this.persistThemeState();
      this.syncThemePanelClass();
    }

    closeThemePanel(): void {
      this.themePanelOpen = false;
      this.themeMenuOpen = false;
      this.persistThemeState();
      this.syncThemePanelClass();
    }

    /** Toggle only the dropdown submenu, without opening the panel */
    toggleThemeMenu(): void {
      this.themeMenuOpen = !this.themeMenuOpen;
      localStorage.setItem(this.themeMenuStorageKey, String(this.themeMenuOpen));
    }

    /** Opens the theme panel and sets the specific section (used from sidebar dropdown) */
    openThemeSection(section: string): void {
      this.activeThemeSection = section;
      this.openThemePanel();
    }

    /** Persists open state so the menu/panel stays open after a reload */
    private persistThemeState(): void {
      try {
        localStorage.setItem(this.themePanelStorageKey, String(this.themePanelOpen));
        localStorage.setItem(this.themeMenuStorageKey, String(this.themeMenuOpen));
      } catch {
        // ignore storage errors
      }
    }

    /** Restores persisted theme menu/panel state from storage */
    private restoreThemeState(): void {
      try {
        const panel = localStorage.getItem(this.themePanelStorageKey);
        const menu = localStorage.getItem(this.themeMenuStorageKey);
        if (panel === 'true') {
          this.themePanelOpen = true;
          this.syncThemePanelClass();
        }
        if (menu === 'true') {
          this.themeMenuOpen = true;
        }
      } catch {
        // ignore storage access errors
      }
    }

  private syncThemePanelClass(): void {
    if (this.themePanelOpen) {
      this.htmlElement.classList.add('theme-panel-open');
    } else {
      this.htmlElement.classList.remove('theme-panel-open');
    }
  }

  private settings = [
    { attribute: 'data-layout', defaultValue: 'vertical' },
    { attribute: 'data-bs-theme', defaultValue: 'light' },
    { attribute: 'data-content-width', defaultValue: 'default' },
    { attribute: 'dir', defaultValue: 'ltr' },
    { attribute: 'data-sidebar-color', defaultValue: 'light' },
    { attribute: 'data-sidebar', defaultValue: 'default' },
    { attribute: 'data-theme-colors', defaultValue: 'default' },
  ];

  init(): void {
    this.settings.forEach(({ attribute, defaultValue }) => {
      const value = localStorage.getItem(attribute) || defaultValue;
      this.htmlElement.setAttribute(attribute, value);
      if (attribute === 'dir') this.updateLayoutDir(value);
      if (attribute === 'data-bs-theme') this.setTheme(value, false);
    });

    this.updateSimpleBar(
      this.htmlElement.getAttribute('data-sidebar') ??
      this.htmlElement.getAttribute('data-layout') ?? 'vertical'
    );

    if (this.htmlElement.getAttribute('data-layout') === 'horizontal') {
      this.removeHorizontalAttributes();
    }

    // Restore persisted theme menu/panel open state (so it survives reloads)
    this.restoreThemeState();

    this.bindEvents();
  }

  private bindEvents(): void {
    // Toggle Sidebar
    document.getElementById('toggleSidebar')?.addEventListener('click', () => {
      const sidebar = this.htmlElement.getAttribute('data-sidebar');
      if (window.innerWidth < 1024) {
        document.getElementById('sidebar')?.classList.add('show');
      } else if (sidebar === 'icon') {
        this.setAndSaveAttribute('data-sidebar', 'default');
      } else {
        this.setAndSaveAttribute('data-sidebar', 'icon');
      }
      this.updateSimpleBar(this.htmlElement.getAttribute('data-layout') ?? 'vertical');
    });

    // Sidebar Backdrop
    document.getElementById('sidebar-backdrop')?.addEventListener('click', () => {
      this.closeThemePanel();
      if (this.htmlElement.getAttribute('data-layout') === 'horizontal') {
        document.getElementById('horizontal-aside')?.classList.toggle('show');
      } else {
        document.getElementById('sidebar')?.classList.remove('show');
      }
    });

    // Theme Toggle
    document.getElementById('theme-toggle')?.addEventListener('click', () => {
      const current = this.htmlElement.getAttribute('data-bs-theme');
      const next = current === 'light' ? 'dark' : 'light';
      this.setAndSaveAttribute('data-bs-theme', next, false);
      this.setTheme(next);
    });

    // Customizer Radio Buttons
    document.querySelectorAll('.layout-customizer input[type="radio"]')
      .forEach(el => el.addEventListener('change', (e) => this.handleRadioChange(e)));

    // Reset Button
    document.getElementById('resetBtn')?.addEventListener('click', () => {
      localStorage.clear();
      window.location.reload();
    });

    // Responsive Sidebar
    this.handleResponsiveSidebar();
    window.addEventListener('resize', () => this.handleResponsiveSidebar());
  }

  setAndSaveAttribute(attr: string, value: string, updateSidebarColor = true): void {
    this.htmlElement.setAttribute(attr, value);
    localStorage.setItem(attr, value);

    if (attr === 'data-sidebar-color' && updateSidebarColor) {
      const theme = this.htmlElement.getAttribute('data-bs-theme');
      if (theme === 'light') localStorage.setItem('sidebar-color-light-mode', value);
      else if (theme === 'dark') localStorage.setItem('sidebar-color-dark-mode', value);
    }

    const radio = document.querySelector<HTMLInputElement>(
      `input[name="${attr}"][value="${value}"]`
    );
    if (radio) radio.checked = true;
  }

  setTheme(theme: string, save = true): void {
    let resolved = theme;
    if (theme === 'auto') {
      resolved = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
      this.htmlElement.setAttribute('data-bs-theme', resolved);
    }

    const lightColor = localStorage.getItem('sidebar-color-light-mode');
    const darkColor = localStorage.getItem('sidebar-color-dark-mode');
    const sidebarColor = resolved === 'light' && lightColor ? lightColor
      : resolved === 'dark' && darkColor ? darkColor
      : resolved === 'dark' ? 'dark' : 'light';

    this.htmlElement.setAttribute('data-sidebar-color', sidebarColor);
    if (save) localStorage.setItem('data-sidebar-color', sidebarColor);

    const toggle = document.getElementById('theme-toggle');
    if (toggle) {
      toggle.setAttribute('aria-label',
        resolved === 'light' ? 'Switch to dark theme' : 'Switch to light theme'
      );
    }
  }

  updateLayoutDir(dir: string): void {
    const bootstrap = document.getElementById('bootstrap-style') as HTMLLinkElement;
    const app = document.getElementById('app-style') as HTMLLinkElement;
    if (!bootstrap || !app) return;

    if (dir === 'rtl') {
      bootstrap.href = bootstrap.href.replace('bootstrap.min.css', 'bootstrap-rtl.min.css');
      app.href = app.href.replace('app.min.css', 'app-rtl.min.css');
    } else {
      bootstrap.href = bootstrap.href.replace('bootstrap-rtl.min.css', 'bootstrap.min.css');
      app.href = app.href.replace('app-rtl.min.css', 'app.min.css');
    }
  }

  updateSimpleBar(layout: string): void {
    const sidebar = document.getElementById('sidebar-simplebar');
    if (!sidebar) return;

    const isIconMode = this.htmlElement.getAttribute('data-sidebar') === 'icon';
    const shouldUnmount = !['vertical','horizontal','default','semibox','medium','icon-hover'].includes(layout) || isIconMode;

    if (shouldUnmount) {
      setTimeout(() => {
        if ((window as any).SimpleBar) {
          const instance = (window as any).SimpleBar.instances.get(sidebar);
          instance?.unMount();
        }
      }, 500);
    } else {
      sidebar.setAttribute('data-simplebar', '');
      if ((window as any).SimpleBar) {
        new (window as any).SimpleBar(sidebar);
      }
    }
  }

  removeHorizontalAttributes(): void {
    this.htmlElement.removeAttribute('data-sidebar');
    this.htmlElement.setAttribute('data-topbar-theme', 'dark');
  }

  private handleRadioChange(event: Event): void {
    const { name, value } = event.target as HTMLInputElement;
    switch (name) {
      case 'data-bs-theme':
        this.setAndSaveAttribute('data-bs-theme', value);
        this.setTheme(value);
        break;
      case 'data-layout':
        this.setAndSaveAttribute('data-layout', value);
        if (value === 'horizontal') {
          this.removeHorizontalAttributes();
        } else {
          this.htmlElement.removeAttribute('data-topbar-theme');
        }
        this.updateSimpleBar(value);
        break;
      case 'data-content-width':
        this.setAndSaveAttribute('data-content-width', value);
        break;
      case 'dir':
        this.setAndSaveAttribute('dir', value);
        this.updateLayoutDir(value);
        break;
      case 'data-sidebar':
        if (this.htmlElement.getAttribute('data-layout') !== 'horizontal') {
          this.setAndSaveAttribute('data-sidebar', value);
        }
        this.updateSimpleBar(value);
        break;
      case 'data-sidebar-color':
        this.setAndSaveAttribute('data-sidebar-color', value, true);
        break;
      case 'data-theme-colors':
        this.setAndSaveAttribute('data-theme-colors', value);
        break;
    }
  }

  private handleResponsiveSidebar(): void {
    if (window.innerWidth < 992) {
      this.htmlElement.removeAttribute('data-sidebar');
    }
  }
}
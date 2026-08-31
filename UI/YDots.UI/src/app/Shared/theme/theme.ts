import { Component, DOCUMENT, inject, OnInit, Renderer2 } from '@angular/core';
import { LayoutService } from '../../Service/layout-service';

interface FontOption {
  name: string;
  value: string;
}

interface WeightOption {
  label: string;
  value: string;
}

interface RailSection {
  key: string;
  icon: string;
  label: string;
}

interface ThemeSettings {
  primaryColor: string;
  secondaryColor: string;
  iconColor: string;
  headingColor: string;
  isDarkMode: boolean;
  layoutMode: string;
  menuFont: string;
  headingFont: string;
  numberFont: string;
  otherFont: string;
  fontSize: string;
  fontWeight: string;
  lineHeight: number;
  shadowStrength: number;
  borderRadius: number;
  buttonRadius: number;
  sidebarBg: string | null;
  profilePhoto: string | null;
}

const STORAGE_KEY = 'app-theme-settings';

const DEFAULT_SETTINGS: ThemeSettings = {
  primaryColor: '#315746',
  secondaryColor: '#305faa',
  iconColor: '#64748b',
  headingColor: '#111827',
  isDarkMode: false,
  layoutMode: 'box',
  menuFont: 'Outfit, sans-serif',
  headingFont: 'Outfit, sans-serif',
  numberFont: 'Outfit, sans-serif',
  otherFont: 'Outfit, sans-serif',
  fontSize: '14px',
  fontWeight: '400',
  lineHeight: 1.6,
  shadowStrength: 15,
  borderRadius: 8,
  buttonRadius: 8,
  sidebarBg: null,
  profilePhoto: null,
};

@Component({
  selector: 'app-theme',
  standalone: true,
  templateUrl: './theme.html',
  styleUrl: './theme.css',
})
export class ThemeComponent implements OnInit {
  private layoutService = inject(LayoutService);
  private renderer = inject(Renderer2);
  private document = inject(DOCUMENT);

  // ── Panel open / active rail section come from the shared LayoutService ──
  get isOpen(): boolean {
    return this.layoutService.themePanelOpen;
  }

  get activeSection(): string {
    return this.layoutService.activeThemeSection;
  }
  set activeSection(value: string) {
    this.layoutService.activeThemeSection = value;
  }

  readonly sections: RailSection[] = [
    { key: 'colors',     icon: 'bi bi-palette2',      label: 'Primary color' },
    { key: 'secondary',  icon: 'bi bi-droplet-half',  label: 'Secondary color' },
    { key: 'mode',       icon: 'bi bi-circle-half',   label: 'Mode' },
    { key: 'layout',     icon: 'bi bi-layout-split',  label: 'Layout' },
    { key: 'fontFamily', icon: 'bi bi-fonts',         label: 'Font family' },
    { key: 'fontStyle',  icon: 'bi bi-type',          label: 'Font size & style' },
    { key: 'appearance', icon: 'bi bi-shadows',       label: 'Shadow & radius' },
    { key: 'icon',       icon: 'bi bi-stars',         label: 'Icon color' },
    { key: 'heading',    icon: 'bi bi-type-h1',       label: 'Heading color' },
    { key: 'background', icon: 'bi bi-image',         label: 'Sidebar background' },
    { key: 'profile',    icon: 'bi bi-person-circle', label: 'Profile photo' },
  ];

  setActive(key: string): void {
    this.activeSection = key;
  }

  openPanel(): void {
    this.layoutService.openThemePanel();
  }

  closePanel(): void {
    this.layoutService.closeThemePanel();
  }

  // ── Toast ──
  toast: { type: 'success' | 'error'; message: string } | null = null;
  private toastTimer: ReturnType<typeof setTimeout> | null = null;

  private showToast(type: 'success' | 'error', message: string): void {
    this.toast = { type, message };
    if (this.toastTimer) clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => (this.toast = null), 3000);
  }

  // ── Live state ──
  settings: ThemeSettings = { ...DEFAULT_SETTINGS };

  // Convenience getters/setters used by the template (kept flat to match markup)
  get primaryColor()   { return this.settings.primaryColor; }
  get secondaryColor() { return this.settings.secondaryColor; }
  get iconColor()       { return this.settings.iconColor; }
  get headingColor()    { return this.settings.headingColor; }
  get isDarkMode()      { return this.settings.isDarkMode; }
  get layoutMode()      { return this.settings.layoutMode; }
  get selectedMenuFont()    { return this.settings.menuFont; }
  get selectedHeadingFont() { return this.settings.headingFont; }
  get selectedNumberFont()  { return this.settings.numberFont; }
  get selectedOtherFont()   { return this.settings.otherFont; }
  get selectedFontSize()    { return this.settings.fontSize; }
  get selectedFontWeight()  { return this.settings.fontWeight; }
  get selectedLineHeight()  { return this.settings.lineHeight; }
  get shadowLevel()         { return this.settings.shadowStrength; }
  get borderRadiusLevel()   { return this.settings.borderRadius; }
  get buttonRadiusLevel()   { return this.settings.buttonRadius; }
  get selectedBg()           { return this.settings.sidebarBg; }
  get selectedProfilePhoto() { return this.settings.profilePhoto; }

  // ── Presets ──
  readonly defaultPrimaryPresets   = ['#315746', '#0085db', '#f97316', '#ef4444', '#8b5cf6', '#10b981', '#fb3cf9'];
  readonly defaultSecondaryPresets = ['#fa896b', '#305faa', '#31f3c2', '#9ac500', '#bd0068', '#00bebe'];
  readonly defaultIconPresets      = ['#64748b', '#f97316', '#ef4444', '#8b5cf6', '#10b981', '#fb3cf9'];
  readonly defaultHeadingPresets   = ['#00af43', '#ff27a5', '#c2a500', '#b8530b', '#ef4444', '#8b5cf6'];

  primaryPresets:   string[] = [];
  secondaryPresets: string[] = [];
  iconPresets:      string[] = [];
  headingPresets:   string[] = [];

  // ── Fonts ──
  readonly fonts: FontOption[] = [
    { name: 'Outfit',     value: 'Outfit, sans-serif' },
    { name: 'Quicksand',  value: 'Quicksand, sans-serif' },
    { name: 'Inter',      value: 'Inter, sans-serif' },
    { name: 'Roboto',     value: 'Roboto, sans-serif' },
    { name: 'Poppins',    value: 'Poppins, sans-serif' },
    { name: 'Nunito',     value: 'Nunito, sans-serif' },
    { name: 'Georgia',    value: 'Georgia, serif' },
    { name: 'Times',      value: 'Times New Roman, serif' },
    { name: 'Courier',    value: 'Courier New, monospace' },
    { name: 'Montserrat', value: 'Montserrat, sans-serif' },
    { name: 'Lato',       value: 'Lato, sans-serif' },
    { name: 'Open Sans',  value: 'Open Sans, sans-serif' },
    { name: 'Raleway',    value: 'Raleway, sans-serif' },
    { name: 'Plus',       value: 'Plus Jakarta Sans' },
  ];

  readonly fontSizes = [12, 13, 14, 15, 16, 18];

  readonly fontWeights: WeightOption[] = [
    { label: 'Light',   value: '300' },
    { label: 'Regular', value: '400' },
    { label: 'Medium',  value: '500' },
    { label: 'Bold',    value: '600' },
    { label: 'Bolder',  value: '700' },
  ];

  // ── Sidebar Backgrounds & Avatars ──
  readonly backgrounds: string[] = [
    'https://picsum.photos/seed/sidebar-slate/400/400?blur=1',
    'https://picsum.photos/seed/sidebar-marble/400/400?blur=1',
    'https://picsum.photos/seed/sidebar-gradient/400/400?blur=1',
    'https://picsum.photos/seed/sidebar-charcoal/400/400?grayscale&blur=1',
    'https://picsum.photos/seed/sidebar-ocean/400/400?blur=1',
    'https://picsum.photos/seed/sidebar-dusk/400/400?blur=1',
  ];

  readonly profileImages: string[] = Array.from(
    { length: 10 },
    (_, i) => `https://picsum.photos/seed/user${i + 1}/100/100`
  );

  ngOnInit(): void {
    this.primaryPresets   = [...this.defaultPrimaryPresets,   ...this.loadCustomColors('custom-primary-colors',   this.defaultPrimaryPresets)];
    this.secondaryPresets = [...this.defaultSecondaryPresets, ...this.loadCustomColors('custom-secondary-colors', this.defaultSecondaryPresets)];
    this.iconPresets      = [...this.defaultIconPresets,      ...this.loadCustomColors('custom-icon-colors',      this.defaultIconPresets)];
    this.headingPresets   = [...this.defaultHeadingPresets,   ...this.loadCustomColors('custom-heading-colors',   this.defaultHeadingPresets)];

    this.loadTheme();

    // If the rail hasn't been pointed at one of this panel's sections yet, default to the first one
    if (!this.sections.some(s => s.key === this.activeSection)) {
      this.activeSection = 'colors';
    }
  }

  // ═══════════ Color helpers ═══════════

  private hexToRgb(hex: string): string {
    const clean = hex.replace('#', '');
    const bigint = parseInt(clean, 16);
    const r = (bigint >> 16) & 255;
    const g = (bigint >> 8) & 255;
    const b = bigint & 255;
    return `${r}, ${g}, ${b}`;
  }

  private isValidHex(value: string): boolean {
    return /^#([0-9A-Fa-f]{6})$/.test(value);
  }

  private isColorAllowed(value: string): boolean {
    if (!this.isValidHex(value)) return false;
    const lower = value.toLowerCase();
    if (!this.settings.isDarkMode && lower === '#ffffff') return false;
    if (this.settings.isDarkMode && lower === '#000000') return false;
    return true;
  }

  private colorErrorMessage(color: string): string {
    if (!this.isValidHex(color)) return 'Invalid color format.';
    return this.settings.isDarkMode
      ? 'Black cannot be used in dark mode.'
      : 'White cannot be used in light mode.';
  }

  private setCssVar(name: string, value: string): void {
    this.renderer.setStyle(this.document.documentElement, name, value, 2 /* DashCase, no important */);
    this.autoPersist();
  }

  // ═══════════ Primary color ═══════════

  setPrimaryPreset(color: string): void {
    this.applyPrimaryColor(color);
  }

  onPrimaryInput(event: Event): void {
    this.applyPrimaryColor((event.target as HTMLInputElement).value);
  }

  private applyPrimaryColor(color: string): void {
    const lower = color.toLowerCase();
    if (!this.isColorAllowed(lower)) {
      this.showToast('error', this.colorErrorMessage(lower));
      return;
    }
    this.settings.primaryColor = lower;
    this.setCssVar('--primary', lower);
    this.setCssVar('--color-primary', lower);
    this.setCssVar('--primary-rgb', this.hexToRgb(lower));
    // Apply globally to the app theme (pe-* variables used by app.min.css)
    this.setCssVar('--pe-primary', lower);
    this.setCssVar('--pe-primary-rgb', this.hexToRgb(lower));
    this.setCssVar('--pe-primary-text-emphasis', lower);
    this.setCssVar('--pe-primary-bg-subtle', `rgba(${this.hexToRgb(lower)}, 0.1)`);
    this.setCssVar('--pe-primary-border-subtle', `rgba(${this.hexToRgb(lower)}, 0.5)`);
    this.setCssVar('--pe-link-color-rgb', this.hexToRgb(lower));
    // Also update sidebar background to use the primary color
    this.applySidebarBg(this.settings.sidebarBg);
  }

  get isCustomPrimary(): boolean {
    return !!this.primaryColor &&
      !this.primaryPresets.map(c => c.toLowerCase()).includes(this.primaryColor.toLowerCase());
  }

  // ═══════════ Secondary color ═══════════

  setSecondaryPreset(color: string): void {
    this.applySecondaryColor(color);
  }

  onSecondaryInput(event: Event): void {
    this.applySecondaryColor((event.target as HTMLInputElement).value);
  }

  private applySecondaryColor(color: string): void {
    const lower = color.toLowerCase();
    if (!this.isColorAllowed(lower)) {
      this.showToast('error', this.colorErrorMessage(lower));
      return;
    }
    this.settings.secondaryColor = lower;
    this.setCssVar('--secondary', lower);
    // Apply globally to the app theme (pe-* variables used by app.min.css)
    this.setCssVar('--pe-secondary', lower);
    this.setCssVar('--pe-secondary-rgb', this.hexToRgb(lower));
    this.setCssVar('--pe-secondary-text-emphasis', lower);
    this.setCssVar('--pe-secondary-bg-subtle', `rgba(${this.hexToRgb(lower)}, 0.1)`);
    this.setCssVar('--pe-secondary-border-subtle', `rgba(${this.hexToRgb(lower)}, 0.5)`);
    this.setCssVar('--pe-secondary-color', lower);
  }

  get isCustomSecondary(): boolean {
    return !!this.secondaryColor &&
      !this.secondaryPresets.map(c => c.toLowerCase()).includes(this.secondaryColor.toLowerCase());
  }

  // ═══════════ Icon color ═══════════

  setIconPreset(color: string): void {
    this.applyIconColor(color);
  }

  onIconInput(event: Event): void {
    this.applyIconColor((event.target as HTMLInputElement).value);
  }

  private applyIconColor(color: string): void {
    const lower = color.toLowerCase();
    if (!this.isColorAllowed(lower)) {
      this.showToast('error', this.colorErrorMessage(lower));
      return;
    }
    this.settings.iconColor = lower;
    this.setCssVar('--icon-color', lower);
  }

  get isCustomIcon(): boolean {
    return !!this.iconColor &&
      !this.iconPresets.map(c => c.toLowerCase()).includes(this.iconColor.toLowerCase());
  }

  // ═══════════ Heading color ═══════════

  setHeadingPreset(color: string): void {
    this.applyHeadingColor(color);
  }

  onHeadingInput(event: Event): void {
    this.applyHeadingColor((event.target as HTMLInputElement).value);
  }

  private applyHeadingColor(color: string): void {
    const lower = color.toLowerCase();
    if (!this.isColorAllowed(lower)) {
      this.showToast('error', this.colorErrorMessage(lower));
      return;
    }
    this.settings.headingColor = lower;
    this.setCssVar('--heading-color', lower);
  }

  get isCustomHeading(): boolean {
    return !!this.headingColor &&
      !this.headingPresets.map(c => c.toLowerCase()).includes(this.headingColor.toLowerCase());
  }

  // ═══════════ Theme mode ═══════════

  setThemeMode(dark: boolean): void {
    this.settings.isDarkMode = dark;
    const theme = dark ? 'dark' : 'light';
    this.layoutService.setAndSaveAttribute('data-bs-theme', theme, false);
    this.layoutService.setTheme(theme);
    this.autoPersist();
  }

  // ═══════════ Layout ═══════════

  selectLayout(mode: 'horizontal' | 'vertical'): void {
    this.settings.layoutMode = mode === 'horizontal' ? 'fluid' : 'box';
    this.layoutService.setAndSaveAttribute('data-layout', mode);
    if (mode === 'horizontal') {
      this.layoutService.removeHorizontalAttributes();
    } else {
      this.document.documentElement.removeAttribute('data-topbar-theme');
    }
    this.layoutService.updateSimpleBar(mode);
    this.autoPersist();
  }

  // ═══════════ Fonts ═══════════

  setFont(type: 'menu' | 'heading' | 'number' | 'other', value: string): void {
    const map: Record<string, string> = {
      menu: '--font-menu',
      heading: '--font-heading',
      number: '--font-number',
      other: '--font-other',
    };
    switch (type) {
      case 'menu':    this.settings.menuFont = value; break;
      case 'heading': this.settings.headingFont = value; break;
      case 'number':  this.settings.numberFont = value; break;
      case 'other':   this.settings.otherFont = value; break;
    }
    this.setCssVar(map[type], value);
    // Apply globally to the app (--font-ui / --font-mono used by styles.css and pages)
    this.setCssVar('--font-ui', value);
    this.setCssVar('--pe-font-family', value);
    this.setCssVar('--pe-font-mono', value);
  }

  setFontSize(size: number): void {
    this.settings.fontSize = `${size}px`;
    this.setCssVar('--font-size-base', this.settings.fontSize);
  }

  setFontWeight(w: string): void {
    this.settings.fontWeight = w;
    this.setCssVar('--font-weight-base', w);
  }

  onLineHeightInput(event: Event): void {
    this.settings.lineHeight = parseFloat((event.target as HTMLInputElement).value);
    this.setCssVar('--line-height-base', this.settings.lineHeight.toString());
  }

  getLineHeightLabel(): string {
    if (this.settings.lineHeight <= 1.3) return 'Tight';
    if (this.settings.lineHeight <= 1.8) return 'Normal';
    return 'Loose';
  }

  // ═══════════ Shadow / Radius ═══════════

  onShadowInput(event: Event): void {
    this.settings.shadowStrength = parseInt((event.target as HTMLInputElement).value, 10);
    this.setCssVar('--shadow-strength', (this.settings.shadowStrength / 100).toFixed(2));
  }

  onBorderRadiusInput(event: Event): void {
    this.settings.borderRadius = parseInt((event.target as HTMLInputElement).value, 10);
    this.setCssVar('--radius', `${this.settings.borderRadius}px`);
  }

  onButtonRadiusInput(event: Event): void {
    this.settings.buttonRadius = parseInt((event.target as HTMLInputElement).value, 10);
    this.setCssVar('--btn-radius', `${this.settings.buttonRadius}px`);
  }

  // ═══════════ Sidebar background ═══════════

  selectBg(bg: string | null): void {
    this.settings.sidebarBg = bg;
    this.applySidebarBg(bg);
    this.autoPersist();
  }

  private applySidebarBg(bg: string | null): void {
    // Use the SAME variable name the CSS class `.theme-bg-active` reads,
    // so the dark gradient overlay in sidebar.css sits on top of the image
    // instead of the inline `background-image` overriding it.
    this.setCssVar('--pe-sidebar-bg-image', bg ? `url(${bg})` : 'none');
    // Set sidebar background to use the primary color as default
    this.setCssVar('--pe-app-sidebar-bg', this.settings.primaryColor);
    const sidebar = this.document.querySelector('.sidebar') as HTMLElement | null;
    if (sidebar) {
      if (bg) {
        sidebar.classList.add('theme-bg-active');
        // Remove the inline background-image so the CSS class gradient overlay
        // (dark overlay) over the image is visible and readable.
        sidebar.style.removeProperty('background-image');
        sidebar.style.backgroundSize = 'cover';
        sidebar.style.backgroundPosition = 'center';
      } else {
        sidebar.classList.remove('theme-bg-active');
        sidebar.style.removeProperty('background-image');
        sidebar.style.removeProperty('background-size');
        sidebar.style.removeProperty('background-position');
        sidebar.style.backgroundColor = '#ffffff';
      }
    }
    // Also apply to the pe-app-sidebar element
    const peSidebar = this.document.querySelector('.pe-app-sidebar') as HTMLElement | null;
    if (peSidebar) {
      if (bg) {
        peSidebar.classList.add('theme-bg-active');
        // Same as above — let the CSS class handle the image + dark overlay.
        peSidebar.style.removeProperty('background-image');
        peSidebar.style.backgroundSize = 'cover';
        peSidebar.style.backgroundPosition = 'center';
      } else {
        peSidebar.classList.remove('theme-bg-active');
        peSidebar.style.removeProperty('background-image');
        peSidebar.style.removeProperty('background-size');
        peSidebar.style.removeProperty('background-position');
        peSidebar.style.backgroundColor = '#ffffff';
      }
    }
  }

  // ═══════════ Profile photo ═══════════

  selectProfile(url: string): void {
    this.settings.profilePhoto = url;
    this.setCssVar('--profile-image', `url(${url})`);
    this.document.querySelectorAll<HTMLImageElement>('img.profile-avatar, img.user-img').forEach(img => {
      img.src = url;
    });
    this.autoPersist();
  }

  // ═══════════ Custom color persistence ═══════════

  private loadCustomColors(key: string, defaults: string[]): string[] {
    try {
      const raw = localStorage.getItem(key);
      if (!raw) return [];
      const parsed: string[] = JSON.parse(raw);
      const defaultsLc = defaults.map(c => c.toLowerCase());
      return parsed.filter(c => !defaultsLc.includes(c.toLowerCase()));
    } catch {
      return [];
    }
  }

  private saveCustomColor(key: string, color: string, defaults: string[], presets: string[]): void {
    const lc = color.toLowerCase();
    const defaultsLc = defaults.map(c => c.toLowerCase());
    const presetsLc = presets.map(c => c.toLowerCase());
    if (!defaultsLc.includes(lc) && !presetsLc.includes(lc)) {
      presets.push(color);
      const custom = presets.filter(c => !defaultsLc.includes(c.toLowerCase()));
      try {
        localStorage.setItem(key, JSON.stringify(custom));
      } catch {
        // ignore storage errors
      }
    }
  }

  // ═══════════ Auto persist / apply all ═══════════

  private autoPersist(): void {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.settings));
    } catch {
      // ignore storage errors
    }
  }

  private applyAll(): void {
    this.setCssVar('--primary', this.settings.primaryColor);
    this.setCssVar('--color-primary', this.settings.primaryColor);
    this.setCssVar('--primary-rgb', this.hexToRgb(this.settings.primaryColor));
    // Apply globally to the app theme (pe-* variables used by app.min.css)
    this.setCssVar('--pe-primary', this.settings.primaryColor);
    this.setCssVar('--pe-primary-rgb', this.hexToRgb(this.settings.primaryColor));
    this.setCssVar('--pe-primary-text-emphasis', this.settings.primaryColor);
    this.setCssVar('--pe-primary-bg-subtle', `rgba(${this.hexToRgb(this.settings.primaryColor)}, 0.1)`);
    this.setCssVar('--pe-primary-border-subtle', `rgba(${this.hexToRgb(this.settings.primaryColor)}, 0.5)`);
    this.setCssVar('--pe-link-color-rgb', this.hexToRgb(this.settings.primaryColor));
    this.setCssVar('--secondary', this.settings.secondaryColor);
    // Apply globally to the app (pe-* variables used by app.min.css)
    this.setCssVar('--pe-secondary', this.settings.secondaryColor);
    this.setCssVar('--pe-secondary-rgb', this.hexToRgb(this.settings.secondaryColor));
    this.setCssVar('--pe-secondary-text-emphasis', this.settings.secondaryColor);
    this.setCssVar('--pe-secondary-bg-subtle', `rgba(${this.hexToRgb(this.settings.secondaryColor)}, 0.1)`);
    this.setCssVar('--pe-secondary-border-subtle', `rgba(${this.hexToRgb(this.settings.secondaryColor)}, 0.5)`);
    this.setCssVar('--pe-secondary-color', this.settings.secondaryColor);
    this.setCssVar('--icon-color', this.settings.iconColor);
    this.setCssVar('--heading-color', this.settings.headingColor);
    this.setCssVar('--font-menu', this.settings.menuFont);
    this.setCssVar('--font-heading', this.settings.headingFont);
    this.setCssVar('--font-number', this.settings.numberFont);
    this.setCssVar('--font-other', this.settings.otherFont);
    // Apply globally to the app (--font-ui / --font-mono used by styles.css and pages)
    this.setCssVar('--font-ui', this.settings.menuFont);
    this.setCssVar('--pe-font-family', this.settings.menuFont);
    this.setCssVar('--pe-font-mono', this.settings.menuFont);
    this.setCssVar('--font-size-base', this.settings.fontSize);
    this.setCssVar('--font-weight-base', this.settings.fontWeight);
    this.setCssVar('--line-height-base', this.settings.lineHeight.toString());
    this.setCssVar('--shadow-strength', (this.settings.shadowStrength / 100).toFixed(2));
    this.setCssVar('--radius', `${this.settings.borderRadius}px`);
    this.setCssVar('--btn-radius', `${this.settings.buttonRadius}px`);

    this.layoutService.setAndSaveAttribute(
      'data-bs-theme',
      this.settings.isDarkMode ? 'dark' : 'light',
      false
    );
    this.layoutService.setAndSaveAttribute(
      'data-layout',
      this.settings.layoutMode === 'fluid' ? 'horizontal' : 'vertical'
    );

    if (this.settings.sidebarBg) {
      this.applySidebarBg(this.settings.sidebarBg);
    }
    if (this.settings.profilePhoto) {
      this.setCssVar('--profile-image', `url(${this.settings.profilePhoto})`);
    }
  }

  // ═══════════ Save / Reset ═══════════

  saveTheme(): void {
    const colors = [this.settings.primaryColor, this.settings.secondaryColor, this.settings.iconColor, this.settings.headingColor];
    if (colors.some(c => !this.isColorAllowed(c))) {
      this.showToast('error', 'Please fix invalid colors before saving.');
      return;
    }

    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.settings));
      this.saveCustomColor('custom-primary-colors',   this.settings.primaryColor,   this.defaultPrimaryPresets,   this.primaryPresets);
      this.saveCustomColor('custom-secondary-colors', this.settings.secondaryColor, this.defaultSecondaryPresets, this.secondaryPresets);
      this.saveCustomColor('custom-icon-colors',      this.settings.iconColor,      this.defaultIconPresets,      this.iconPresets);
      this.saveCustomColor('custom-heading-colors',   this.settings.headingColor,   this.defaultHeadingPresets,   this.headingPresets);
      this.showToast('success', 'Theme applied successfully.');
      this.closePanel();
    } catch {
      this.showToast('error', 'Could not save theme to this browser.');
    }
  }

  resetTheme(): void {
    localStorage.removeItem(STORAGE_KEY);
    this.settings = { ...DEFAULT_SETTINGS };
    this.applyAll();
    this.renderer.removeStyle(this.document.documentElement, '--pe-sidebar-bg-image');
    this.renderer.removeStyle(this.document.documentElement, '--pe-app-sidebar-bg');
    this.applySidebarBg(null);
    this.showToast('success', 'Theme reset to defaults.');
  }

  private loadTheme(): void {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (raw) {
        this.settings = { ...DEFAULT_SETTINGS, ...JSON.parse(raw) };
      }
    } catch {
      this.settings = { ...DEFAULT_SETTINGS };
    }
    this.applyAll();
  }
}
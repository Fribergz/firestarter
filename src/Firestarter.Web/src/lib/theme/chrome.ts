import type { Theme } from './types';

/** How breadcrumb segments (after home) are cased. */
export type BreadcrumbSegmentCasing = 'preserve' | 'upper' | 'lower';

/** Typography density for the breadcrumb row. */
export type BreadcrumbTypography = 'default' | 'wide-track';

/** Start screen hero / KPI layout family. */
export type StartHeroTone = 'editorial' | 'industrial' | 'terminal';

/** Section labels, settings headings, table captions: sentence vs all-caps. */
export type SectionLabelCasing = 'sentence' | 'upper';

/** READY chip in the status bar. */
export type StatusReadyPresentation = 'serif-italic' | 'upper-accent' | 'pill-on-accent';

/**
 * Theme-driven UI capabilities — generic flags, not named after a palette.
 * Each {@link Theme} selects a profile; components read `theme.chrome` only.
 */
export type UiChrome = {
  /** Narrow secondary nav column on multi-column list screens. */
  navSidebar: boolean;
  /** List routes: warm canvas bg, editorial page header padding (e.g. wide horizontal inset). */
  editorialListShell: boolean;
  /** Tables on those list routes: hairline rows, editorial header row, wider cell padding. */
  editorialDataList: boolean;
  /** Start route layout: KPI strip + centered editorial hero vs compact centered column. */
  startHeroTone: StartHeroTone;
  /** Primary page `<h1>` uses display serif sizing. */
  pageHeaderSerifTitle: boolean;
  /** When a header trailing slot exists, align it with the title row (flex-end). */
  pageHeaderTrailingAlignWithTitle: boolean;
  breadcrumbSegmentCasing: BreadcrumbSegmentCasing;
  breadcrumbTypography: BreadcrumbTypography;
  sectionLabelCasing: SectionLabelCasing;
  /** Title bar: warm strip, serif wordmark region, taller row. */
  warmTitleBar: boolean;
  /** Title bar route caption uses sentence case instead of uppercase. */
  titleBarRouteSentenceCase: boolean;
  statusBarBackground: 'paper' | 'panel-2';
  statusReadyPresentation: StatusReadyPresentation;
  /** Bottom Start KPI strip & similar numerals: light vs normal weight. */
  kpiNumeralFontWeight: 'light' | 'normal';
  /** Inline hints (e.g. MR branch line) use accent/info tint instead of mute. */
  detailMetaUsesInfoTint: boolean;
};

export const CHROME_BY_THEME: Record<Theme, UiChrome> = {
  ember: {
    navSidebar: true,
    editorialListShell: true,
    editorialDataList: true,
    startHeroTone: 'editorial',
    pageHeaderSerifTitle: true,
    pageHeaderTrailingAlignWithTitle: true,
    breadcrumbSegmentCasing: 'preserve',
    breadcrumbTypography: 'default',
    sectionLabelCasing: 'sentence',
    warmTitleBar: true,
    titleBarRouteSentenceCase: true,
    statusBarBackground: 'paper',
    statusReadyPresentation: 'serif-italic',
    kpiNumeralFontWeight: 'normal',
    detailMetaUsesInfoTint: false,
  },
  graphite: {
    navSidebar: false,
    editorialListShell: false,
    editorialDataList: false,
    startHeroTone: 'industrial',
    pageHeaderSerifTitle: false,
    pageHeaderTrailingAlignWithTitle: false,
    breadcrumbSegmentCasing: 'upper',
    breadcrumbTypography: 'wide-track',
    sectionLabelCasing: 'upper',
    warmTitleBar: false,
    titleBarRouteSentenceCase: false,
    statusBarBackground: 'paper',
    statusReadyPresentation: 'upper-accent',
    kpiNumeralFontWeight: 'light',
    detailMetaUsesInfoTint: false,
  },
  obsidian: {
    navSidebar: false,
    editorialListShell: false,
    editorialDataList: false,
    startHeroTone: 'terminal',
    pageHeaderSerifTitle: false,
    pageHeaderTrailingAlignWithTitle: false,
    breadcrumbSegmentCasing: 'lower',
    breadcrumbTypography: 'default',
    sectionLabelCasing: 'upper',
    warmTitleBar: false,
    titleBarRouteSentenceCase: false,
    statusBarBackground: 'panel-2',
    statusReadyPresentation: 'pill-on-accent',
    kpiNumeralFontWeight: 'normal',
    detailMetaUsesInfoTint: true,
  },
};

export function formatBreadcrumbSegment(chrome: UiChrome, segment: string): string {
  switch (chrome.breadcrumbSegmentCasing) {
    case 'upper':
      return segment.toUpperCase();
    case 'lower':
      return segment.toLowerCase();
    default:
      return segment;
  }
}

export function breadcrumbRowInlineStyle(chrome: UiChrome): string {
  const base = 'font-family:var(--font-mono); font-size:11px;';
  if (chrome.breadcrumbTypography === 'wide-track') {
    return `${base} letter-spacing:.08em; font-size:10.5px;`;
  }
  return base;
}

/** Primary route heading (`<h1>`) — full page vs in-flow detail. */
export function primaryHeadingStyle(chrome: UiChrome, variant: 'page' | 'detail'): string {
  if (chrome.pageHeaderSerifTitle) {
    return variant === 'page'
      ? 'font-family:var(--font-serif); font-size:28px; font-weight:400; letter-spacing:-.01em;'
      : 'font-family:var(--font-serif); font-size:26px; font-weight:400; letter-spacing:-.01em; line-height:1.12;';
  }
  return variant === 'page'
    ? 'font-size:22px; font-weight:500; letter-spacing:-.01em;'
    : 'font-size:20px; font-weight:500; letter-spacing:-.01em; line-height:1.15;';
}

export function startHeroInlineStyle(chrome: UiChrome): string {
  switch (chrome.startHeroTone) {
    case 'editorial':
      return 'font-family:var(--font-serif); font-size:54px; font-weight:400; letter-spacing:-.02em; line-height:1.05;';
    case 'industrial':
      return 'font-family:var(--font-sans); font-size:34px; font-weight:300; letter-spacing:-.02em;';
    case 'terminal':
      return 'font-family:var(--font-mono); font-size:24px; font-weight:500; letter-spacing:-.01em;';
  }
}

export function kpiNumeralFontWeightCss(chrome: UiChrome): string {
  return chrome.kpiNumeralFontWeight === 'light' ? '300' : '400';
}

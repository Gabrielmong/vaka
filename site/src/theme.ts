import { createTheme } from '@mui/material/styles';

/** Colours taken from the Duo template the hero plays, so the page and the panel read as one thing. */
export const colors = {
  page: '#0f1620',
  surface: '#17212d',
  line: 'rgba(230, 237, 243, 0.1)',
  text: '#e6edf3',
  muted: '#93a3b8',
  cpu: '#4aa3ff',
  gpu: '#5ad17a',
};

const display = '"Barlow Semi Condensed", "Bahnschrift", "Segoe UI", system-ui, sans-serif';

export const theme = createTheme({
  palette: {
    mode: 'dark',
    primary: { main: colors.cpu, contrastText: '#06121f' },
    success: { main: colors.gpu },
    background: { default: colors.page, paper: colors.surface },
    text: { primary: colors.text, secondary: colors.muted },
    divider: colors.line,
  },
  shape: { borderRadius: 10 },
  typography: {
    fontFamily: '"Barlow", "Segoe UI", system-ui, sans-serif',
    fontSize: 15,
    h1: { fontFamily: display, fontWeight: 600, fontSize: 'clamp(2.6rem, 6vw, 4.2rem)', lineHeight: 1.02, letterSpacing: '-0.01em' },
    h2: { fontFamily: display, fontWeight: 600, fontSize: 'clamp(1.8rem, 3.6vw, 2.4rem)', lineHeight: 1.1 },
    h3: { fontFamily: display, fontWeight: 600, fontSize: '1.25rem', lineHeight: 1.2 },
    body1: { lineHeight: 1.6 },
    button: { fontFamily: display, fontWeight: 600, fontSize: '1.05rem', textTransform: 'none', letterSpacing: '0.01em' },
  },
  components: {
    MuiCssBaseline: { styleOverrides: { body: { backgroundColor: colors.page }, 'a': { color: colors.cpu } } },
    MuiButton: { defaultProps: { disableElevation: true } },
  },
});

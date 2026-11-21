import { createTheme } from '@mui/material/styles';

export const theme = createTheme({
  palette: {
    mode: 'dark',
    primary: { main: '#5aa9ff' },
    secondary: { main: '#b388ff' },
    warning: { main: '#ffb040' },
    error: { main: '#ff5a5a' },
    success: { main: '#5ad17a' },
    background: { default: '#0d0e11', paper: '#15171c' },
    divider: 'rgba(255,255,255,0.08)',
  },
  shape: { borderRadius: 8 },
  typography: {
    fontFamily: 'Roboto, "Segoe UI", system-ui, sans-serif',
    fontSize: 13,
    overline: { fontWeight: 600, letterSpacing: '0.08em', lineHeight: 2 },
  },
  components: {
    MuiTextField: { defaultProps: { size: 'small', fullWidth: true } },
    MuiFormControl: { defaultProps: { size: 'small' } },
    MuiButton: {
      defaultProps: { size: 'small', disableElevation: true },
      styleOverrides: { root: { textTransform: 'none', fontWeight: 500 } },
    },
    MuiIconButton: { defaultProps: { size: 'small' } },
    MuiToggleButton: { defaultProps: { size: 'small' } },
    MuiTooltip: { defaultProps: { arrow: true, enterDelay: 400 } },
    MuiPaper: { styleOverrides: { root: { backgroundImage: 'none' } } },
    MuiListItemButton: { styleOverrides: { root: { borderRadius: 6 } } },
    MuiMenuItem: { defaultProps: { dense: true } },
    MuiSwitch: { defaultProps: { size: 'small' } },
    MuiCheckbox: { defaultProps: { size: 'small' } },
  },
});

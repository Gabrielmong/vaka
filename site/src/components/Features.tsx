import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import { colors } from '../theme';
import { Section } from './Section';

const FEATURES: [string, string][] = [
  ['Finds your hardware', 'When you pick a layout, CPU and GPU temperatures, loads, clocks and power, fans and pump, RAM, VRAM, drives and network are matched to your PC’s sensors. Anything your PC doesn’t have shows “--”.'],
  ['Edit it in your browser', 'Open the editor from the tray icon. Drag, resize and restyle with a live preview; saving updates the panel at once.'],
  ['FPS of the game in front', 'Frame rate, 1% lows and frame time of the focused app, measured with Intel PresentMon, which only runs while a layout shows FPS.'],
  ['Fits small landscape screens', 'Layouts are drawn at 800×480 and stretch to other panel sizes. Several layouts can take turns as pages.'],
  ['Starts with Windows', 'It runs at logon with the access it needs to read sensors, without a UAC prompt each time.'],
  ['Light on your PC', 'A native Windows app that redraws once a second, so it stays out of your games’ way.'],
];

export function Features() {
  return (
    <Section id="features" title="What it does">
      <Box component="dl" sx={{ m: 0, display: 'grid', columnGap: 6, rowGap: 4, gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' } }}>
        {FEATURES.map(([title, text]) => (
          <Box key={title} sx={{ borderTop: `1px solid ${colors.line}`, pt: 2.5 }}>
            <Typography component="dt" variant="h3" sx={{ mb: 1 }}>{title}</Typography>
            <Typography component="dd" color="text.secondary" sx={{ m: 0, maxWidth: '58ch' }}>{text}</Typography>
          </Box>
        ))}
      </Box>
    </Section>
  );
}

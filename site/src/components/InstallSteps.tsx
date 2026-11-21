import Box from '@mui/material/Box';
import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import type { ReactNode } from 'react';
import { releasesUrl } from '../links';
import { colors } from '../theme';
import { Section } from './Section';

const STEPS: [string, ReactNode][] = [
  ['Download the setup', <>Get <b>VakaSetup.exe</b> from the <Link href={releasesUrl}>latest release</Link>.</>],
  ['Run it', <>Accept one administrator prompt. It installs the panel, PresentMon for FPS and, if you don’t have it yet, the PawnIO sensor driver. The setup isn’t code-signed yet, so if Windows SmartScreen warns, choose <b>More info</b>, then <b>Run anyway</b>.</>],
  ['Pick a layout', <>The panel starts right away and at every logon. Open the editor from its tray icon to choose a layout or make your own.</>],
];

export function InstallSteps() {
  return (
    <Section id="install" title="Install" lead="About a minute. The first install needs internet to fetch the sensor driver.">
      <Box component="ol" sx={{ listStyle: 'none', p: 0, m: 0, display: 'grid', gap: 4, gridTemplateColumns: { xs: '1fr', md: 'repeat(3, 1fr)' } }}>
        {STEPS.map(([title, text], i) => (
          <Box component="li" key={title} sx={{ display: 'grid', gridTemplateColumns: 'auto 1fr', columnGap: 2 }}>
            <Typography aria-hidden sx={{ fontFamily: 'h2.fontFamily', fontWeight: 600, fontSize: '2.4rem', lineHeight: 1, color: colors.cpu }}>{i + 1}</Typography>
            <Box>
              <Typography variant="h3" component="h3" sx={{ mb: 1, mt: 0.5 }}>{title}</Typography>
              <Typography color="text.secondary">{text}</Typography>
            </Box>
          </Box>
        ))}
      </Box>
    </Section>
  );
}

import Box from '@mui/material/Box';
import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { licenseUrl, noticesUrl, repoUrl } from '../links';
import { colors } from '../theme';

export function SiteFooter() {
  return (
    <Box component="footer" sx={{ borderTop: `1px solid ${colors.line}` }}>
      <Box sx={{ maxWidth: 1180, mx: 'auto', px: { xs: 2, sm: 4 }, py: 5, display: 'grid', gap: 3, gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' } }}>
        <Typography variant="body2" color="text.secondary">
          Needs Windows 10 or 11 (64-bit). Don’t run it alongside another tool that reads the motherboard’s sensor chip.
        </Typography>
        <Typography variant="body2" color="text.secondary">
          Built on <Link href="https://github.com/LibreHardwareMonitor/LibreHardwareMonitor">LibreHardwareMonitor</Link>,{' '}
          <Link href="https://github.com/GameTechDev/PresentMon">PresentMon</Link> and{' '}
          <Link href="https://github.com/namazso/PawnIO">PawnIO</Link>. The panels on this page are drawn by the app itself
          from simulated sensors. <Link href={licenseUrl}>MIT licence</Link>, <Link href={noticesUrl}>third-party notices</Link>,{' '}
          <Link href={repoUrl}>source code</Link>.
        </Typography>
      </Box>
    </Box>
  );
}

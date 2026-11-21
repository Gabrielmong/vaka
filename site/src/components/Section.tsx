import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import type { ReactNode } from 'react';

/** A page section: a heading, an optional lead line, then content, on the shared column. */
export function Section({ id, title, lead, children }: { id: string; title: string; lead?: string; children: ReactNode }) {
  return (
    <Box component="section" id={id} aria-labelledby={`${id}-title`}
      sx={{ maxWidth: 1180, mx: 'auto', px: { xs: 2, sm: 4 }, py: { xs: 7, md: 10 } }}>
      <Typography id={`${id}-title`} variant="h2" component="h2" sx={{ mb: lead ? 1.5 : 4 }}>{title}</Typography>
      {lead && <Typography color="text.secondary" sx={{ maxWidth: '62ch', mb: 5 }}>{lead}</Typography>}
      {children}
    </Box>
  );
}

import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogContentText from '@mui/material/DialogContentText';
import DialogTitle from '@mui/material/DialogTitle';
import Snackbar from '@mui/material/Snackbar';
import TextField from '@mui/material/TextField';
import { useEffect, useState } from 'react';
import { getState, setState, useEditor } from '../store';

/** Promise-based prompt/confirm (see askText / askConfirm in the store). */
export function PromptDialog() {
  const d = useEditor(s => s.dialog);
  const [value, setValue] = useState('');
  useEffect(() => { if (d) setValue(d.value ?? ''); }, [d]);

  const close = (result: string | boolean | null) => {
    const cur = getState().dialog;
    setState({ dialog: null });
    cur?.resolve(result);
  };

  return (
    <Dialog open={!!d} onClose={() => close(null)} maxWidth="xs" fullWidth>
      {d && (
        <form onSubmit={e => { e.preventDefault(); close(d.kind === 'prompt' ? value : true); }}>
          <DialogTitle>{d.title}</DialogTitle>
          <DialogContent>
            {d.message && <DialogContentText sx={{ mb: d.kind === 'prompt' ? 2 : 0 }}>{d.message}</DialogContentText>}
            {d.kind === 'prompt' && (
              <TextField autoFocus label={d.label} value={value} onChange={e => setValue(e.target.value)}
                slotProps={{ htmlInput: { spellCheck: false } }} sx={{ mt: 1 }} />
            )}
          </DialogContent>
          <DialogActions>
            <Button onClick={() => close(null)}>Cancel</Button>
            <Button type="submit" variant="contained" color={d.danger ? 'error' : 'primary'}>{d.confirmText ?? 'OK'}</Button>
          </DialogActions>
        </form>
      )}
    </Dialog>
  );
}

export function Toasts() {
  const t = useEditor(s => s.toast);
  const [open, setOpen] = useState(false);
  useEffect(() => { if (t) setOpen(true); }, [t]);
  return (
    <Snackbar open={open && !!t} autoHideDuration={t?.kind === 'error' ? 8000 : 3500} onClose={(_, reason) => { if (reason !== 'clickaway') setOpen(false); }}
      anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}>
      <Alert severity={t?.kind ?? 'info'} variant="filled" onClose={() => setOpen(false)} sx={{ alignItems: 'center' }}>
        {t?.text}
      </Alert>
    </Snackbar>
  );
}

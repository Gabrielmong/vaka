import CloseIcon from '@mui/icons-material/Close';
import FileUploadIcon from '@mui/icons-material/FileUpload';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import ButtonBase from '@mui/material/ButtonBase';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import { useRef, useState } from 'react';
import { deleteImage, uploadImage } from '../actions';
import { editEl, getState, type Sel, setProp, useEditor } from '../store';
import type { ImageEl, ImageInfo } from '../types';
import { Grid2, Help, NumField, Section, SelectField } from './fields';

/** Points the element at an image and fixes its height to the picture's aspect ratio. */
function applyImage(sel: NonNullable<Sel>, img: ImageInfo) {
  editEl(sel, e => {
    e.src = img.name;
    if (img.width > 0 && (e.w as number) > 0) e.h = Math.round((e.w as number) * img.height / img.width);
  });
}

export function ImageSection({ sel, el }: { sel: NonNullable<Sel>; el: ImageEl }) {
  const images = useEditor(s => s.images);
  const fileRef = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const current = images?.find(i => i.name === el.src);

  const upload = async (file: File) => {
    setBusy(true);
    const name = await uploadImage(file);
    setBusy(false);
    if (!name) return;
    const img = getState().images?.find(i => i.name === name);
    if (img) applyImage(sel, img); else setProp(sel, 'src', name);
  };

  return (
    <Section title="Image" action={
      <Button startIcon={<FileUploadIcon />} disabled={busy} onClick={() => fileRef.current?.click()}>
        {busy ? 'Uploading…' : 'Upload'}
      </Button>
    }>
      <input ref={fileRef} type="file" hidden accept="image/png,image/jpeg,image/webp,image/gif,image/bmp" onChange={e => {
        const f = e.target.files?.[0];
        if (f) void upload(f);
        e.target.value = '';
      }} />
      {!images?.length
        ? <Help>No pictures yet — upload a PNG, JPEG, WebP, GIF or BMP (up to 20 MB; large images are shrunk to 4096 px).</Help>
        : (
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(84px, 1fr))', gap: 1 }}>
            {images.map(img => (
              <Box key={img.name} sx={{ position: 'relative', '&:hover .del': { opacity: 1 } }}>
                <ButtonBase onClick={() => applyImage(sel, img)} title={`${img.name} · ${img.width}×${img.height}`}
                  sx={{
                    width: '100%', aspectRatio: '1', borderRadius: 1, border: 2, overflow: 'hidden',
                    borderColor: img.name === el.src ? 'primary.main' : 'divider',
                    backgroundImage: 'repeating-conic-gradient(#2a2c33 0 25%, #1d1f24 0 50%)', backgroundSize: '12px 12px',
                  }}>
                  {img.thumb && <Box component="img" src={img.thumb} alt="" sx={{ maxWidth: '100%', maxHeight: '100%' }} />}
                </ButtonBase>
                <IconButton className="del" size="small" title="Delete image file" onClick={() => void deleteImage(img.name)}
                  sx={{ position: 'absolute', top: 2, right: 2, opacity: 0, bgcolor: 'background.paper', '&:hover': { bgcolor: 'background.paper' } }}>
                  <CloseIcon sx={{ fontSize: 14 }} />
                </IconButton>
              </Box>
            ))}
          </Box>
        )}
      {el.src && (
        <Typography variant="caption" color={current ? 'text.secondary' : 'error'} noWrap>
          {current ? `${current.name} · ${current.width}×${current.height}` : `“${el.src}” is missing`}
        </Typography>
      )}
      <Grid2>
        <SelectField label="Fit" value={el.fit ?? 'contain'}
          options={[['contain', 'Contain'], ['cover', 'Cover (crop)'], ['stretch', 'Stretch']]}
          onChange={v => setProp(sel, 'fit', v)} />
        <NumField label="Corner radius" value={el.radius} min={0} onChange={v => setProp(sel, 'radius', v)} />
      </Grid2>
      {current && (
        <Button size="small" sx={{ alignSelf: 'flex-start' }} onClick={() => applyImage(sel, current)}>Match the picture’s aspect ratio</Button>
      )}
    </Section>
  );
}

import { useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useForm, useWatch } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { Trash01 } from '@untitledui/icons';
import { api } from '../../../api/client';
import { Button } from '@/components/base/buttons/button';
import { Alert } from '../../atoms/Alert';
import { TextField } from '../../atoms/FormFields';
import { Notice } from '../../atoms/Notice';
import { Card } from '../../molecules/Card';
import { ConfirmDialog } from '../../molecules/ConfirmDialog';
import { queryClient } from '../../../lib/query-client';
import { data } from '../../../lib/request';
import type { WorkspaceDeleted, WorkspaceDeletion } from '../../../lib/types';

/**
 * Deleting the whole workspace (#125), Owners only: the same steps as the operator's script. The dialog shows what
 * goes and what blocks it, and the user types the slug; the API checks the slug and the blockers again.
 */
export function DeleteWorkspacePanel() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const tenantId = localStorage.getItem('tenant-id');
  const [open, setOpen] = useState(false);
  const form = useForm({ defaultValues: { slug: '' } });
  const typed = useWatch({ control: form.control, name: 'slug' });
  const report = useQuery({
    queryKey: ['workspace-deletion', tenantId],
    queryFn: () => data<WorkspaceDeletion>(api.GET('/api/v1/tenants/current/deletion')),
    enabled: open,
    // Always fresh: a station may have gone live since the dialog was last open.
    staleTime: 0,
    gcTime: 0,
  });
  const remove = useMutation({
    mutationFn: (confirmSlug: string) =>
      data<WorkspaceDeleted>(api.DELETE('/api/v1/tenants/current', { body: { confirmSlug } })),
    // A 409 means something changed since the report (a DJ went live): show the blocker it found.
    onError: () => void report.refetch(),
  });
  const close = () => {
    setOpen(false);
    remove.reset();
    form.reset();
  };
  const leave = () => {
    // The workspace is gone: the app picks another one, or offers to create one.
    localStorage.removeItem('tenant-id');
    queryClient.clear();
    navigate('/', { replace: true });
  };
  const value = report.data;
  const blocked = (value?.blockers.length ?? 0) > 0;
  const orphans = value?.members.filter((m) => m.leftWithoutWorkspace).length ?? 0;
  const listed = value?.stations.filter((s) => s.listedOnRadioBrowser && !s.deleted).length ?? 0;
  return (
    <Card title={t('workspaceDeletion.title')} description={t('workspaceDeletion.hint')}>
      <div>
        <Button
          color="secondary-destructive"
          size="md"
          iconLeading={Trash01}
          onPress={() => setOpen(true)}
        >
          {t('workspaceDeletion.delete')}
        </Button>
      </div>
      {open && remove.data && (
        <ConfirmDialog
          title={t('workspaceDeletion.doneTitle')}
          confirmLabel={t('workspaceDeletion.continue')}
          onConfirm={leave}
          onCancel={leave}
        >
          <p>
            {t('workspaceDeletion.done', {
              stations: Number(remove.data.counts.stations),
              tracks: Number(remove.data.counts.tracks),
              members: Number(remove.data.counts.members),
            })}
          </p>
        </ConfirmDialog>
      )}
      {open && !remove.data && (
        <ConfirmDialog
          title={t('workspaceDeletion.confirmTitle', { name: value?.name ?? '…' })}
          confirmLabel={t('workspaceDeletion.delete')}
          busy={remove.isPending}
          confirmDisabled={!value || blocked || typed.trim() !== value.slug}
          onConfirm={() => value && remove.mutate(typed.trim())}
          onCancel={close}
        >
          <Notice error={report.error} />
          {value && (
            <>
              {value.blockers.map((blocker) => (
                <Alert
                  key={blocker}
                  color="warning"
                  title={t(`workspaceDeletion.blockers.${blocker}`)}
                />
              ))}
              <p>
                {t('workspaceDeletion.goes', {
                  stations: Number(value.counts.stations),
                  tracks: Number(value.counts.tracks),
                  playlists: Number(value.counts.playlists),
                  members: Number(value.counts.members),
                })}
              </p>
              {orphans > 0 && <p>{t('workspaceDeletion.orphans', { count: orphans })}</p>}
              {listed > 0 && <p>{t('workspaceDeletion.listed', { count: listed })}</p>}
              <p>{t('workspaceDeletion.stays')}</p>
              <TextField
                control={form.control}
                name="slug"
                label={t('workspaceDeletion.typeSlug', { slug: value.slug })}
                autoComplete="off"
              />
            </>
          )}
          <Notice error={remove.error} />
        </ConfirmDialog>
      )}
    </Card>
  );
}

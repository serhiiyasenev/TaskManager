import { afterEach, describe, expect, it, vi } from 'vitest';
import { z } from 'zod';
import { request } from '../../lib/api';
import { TeamInvitationSchema } from '../../lib/contracts';

const route = '/api/teams/17/invitations';
// Native SQL datetime2 reads can serialize without a Z suffix. Keep the native
// timestamp strings while validating every required field and nullable type.
const invitation = {
  id: 'ebc8a8c0-ae46-4a94-adb9-4de46620c14e', email: 'invited@example.test',
  createdAtUtc: '2026-10-12T09:00:00', expiresAtUtc: '2026-10-19T09:00:00',
};

function respond(record: unknown) {
  const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify([record]),
    { status: 200, headers: { 'Content-Type': 'application/json' } }));
  vi.stubGlobal('fetch', fetcher);
  return () => request(route, z.array(TeamInvitationSchema));
}
afterEach(() => { vi.unstubAllGlobals(); });

describe('native invitation JSON through the real request boundary', () => {
  it('reads a fresh invitation when the API omits both null lifecycle timestamps', async () => {
    const read = respond(invitation);
    await expect(read()).resolves.toEqual([{ ...invitation, acceptedAtUtc: null, revokedAtUtc: null }]);
  });

  it('reads an accepted invitation when only the accepted timestamp is serialized', async () => {
    const acceptedAtUtc = '2026-10-12T09:15:00';
    const read = respond({ ...invitation, acceptedAtUtc });
    await expect(read()).resolves.toEqual([{ ...invitation, acceptedAtUtc, revokedAtUtc: null }]);
  });

  it('reads a revoked invitation when only the revoked timestamp is serialized', async () => {
    const revokedAtUtc = '2026-10-12T09:15:00Z';
    const read = respond({ ...invitation, revokedAtUtc });
    await expect(read()).resolves.toEqual([{ ...invitation, acceptedAtUtc: null, revokedAtUtc }]);
  });

  it('keeps explicit null lifecycle timestamps compatible', async () => {
    const record = { ...invitation, acceptedAtUtc: null, revokedAtUtc: null };
    await expect(respond(record)()).resolves.toEqual([record]);
  });

  it.each([
    ['invalid invitation ID', { id: 'not-a-guid' }],
    ['non-string email', { email: 17 }],
    ['non-string creation timestamp', { createdAtUtc: true }],
    ['non-string expiry timestamp', { expiresAtUtc: 17 }],
    ['non-string accepted timestamp', { acceptedAtUtc: {} }],
    ['non-string revoked timestamp', { revokedAtUtc: false }],
    ['missing invitation ID', { id: undefined }],
    ['missing email', { email: undefined }],
    ['missing creation timestamp', { createdAtUtc: undefined }],
    ['missing expiry timestamp', { expiresAtUtc: undefined }],
  ])('rejects %s as an unsafe HTTP 200 response', async (_label, changed) => {
    const read = respond({ ...invitation, acceptedAtUtc: null, revokedAtUtc: null, ...changed });
    await expect(read()).rejects.toMatchObject({ name: 'ApiError', status: 502 });
  });
});

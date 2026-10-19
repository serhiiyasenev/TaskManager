import { Link } from 'react-router-dom';
import { z } from 'zod';
import { Empty, ErrorNotice, Loading } from '../../components/Feedback';
import { ApiError, request } from '../../lib/api';
import { TeamSchema, UserSchema } from '../../lib/contracts';
import { useSession } from '../../lib/session';
import { useLoad } from '../../lib/useLoad';
import { useWorkCopy } from '../work/workCopy';

export function UsersPage() {
  const { user } = useSession();
  if (!user?.roles.some(role => role.toLowerCase() === 'admin')) return <ErrorNotice error={new ApiError(403)} />;
  return <UserDirectory />;
}

function UserDirectory() {
  const { copy, text } = useWorkCopy();
  const load = useLoad('admin-user-directory', async signal => {
    const [users, teams] = await Promise.all([
      request('/api/users', z.array(UserSchema), { signal }),
      request('/api/teams', z.array(TeamSchema), { signal }),
    ]);
    return { users, teams };
  });

  return <>
    <header className="page-header"><div><p className="eyebrow">{copy.administration}</p><h1>{copy.users}</h1>
      {load.data && !load.error && <p>{text('userCount', { count: load.data.users.length })}</p>}
    </div></header>
    {load.loading ? <Loading /> : load.error ? <ErrorNotice error={load.error} retry={() => void load.reload()} />
      : !load.data?.users.length ? <Empty title={copy.noUsers}><p>{copy.usersEmptyIntro}</p></Empty>
        : <div className="panel table-wrap" role="region" aria-label={copy.systemUsers} tabIndex={0}>
          <table><thead><tr><th scope="col">{copy.personName}</th><th scope="col">{copy.username}</th><th scope="col">{copy.email}</th><th scope="col">{copy.team}</th></tr></thead>
            <tbody>{load.data.users.map(user => <tr key={user.id}>
              <td>{`${user.firstName} ${user.lastName}`.trim() || user.userName}</td><td>{user.userName}</td><td>{user.email}</td>
              <td>{user.teamId === null ? copy.noTeam : <Link to={`/teams/${user.teamId}`}>
                {load.data!.teams.find(team => team.id === user.teamId)?.name ?? text('teamNumber', { id: user.teamId })}
              </Link>}</td>
            </tr>)}</tbody>
          </table>
        </div>}
  </>;
}

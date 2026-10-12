export const notificationsCopy = {
  uk: {
    title: 'Сповіщення', eyebrow: 'Ваша скринька', subtitle: 'Нагадування та зміни у доступних вам завданнях.',
    unreadSummary: 'Непрочитані сповіщення', unreadCaption: 'у доступних мені завданнях',
    refresh: 'Оновити сповіщення', unreadOnly: 'Лише непрочитані', total: 'Усього доступних', unreadCount: 'Непрочитаних',
    empty: 'Сповіщень поки немає', emptyHelp: 'Нові сповіщення з’являться тут. Ви можете продовжувати працювати із завданнями.',
    emptyUnread: 'Непрочитаних сповіщень немає', emptyUnreadHelp: 'Перегляньте всі сповіщення або поверніться до завдань.',
    kinds: ['Завдання виконано', 'Нагадування про строк', 'Завдання прострочено', 'Нове завдання'],
    read: 'Прочитано', unread: 'Непрочитане', mark: 'Позначити прочитаним', project: 'Проєкт',
    connecting: 'Встановлюємо зв’язок наживо…', connected: 'Зв’язок наживо встановлено.', reconnecting: 'Відновлюємо зв’язок…',
    offline: 'Немає з’єднання наживо. Оновіть сповіщення, щоб перевірити поточний стан.', reconnect: 'Відновити з’єднання',
    cached: 'Показано раніше завантажені сповіщення. Оновіть дані, коли зв’язок відновиться.',
    tasks: 'Перейти до завдань', anonymous: 'Увійдіть, щоб переглянути свої сповіщення.',
  },
  en: {
    title: 'Notifications', eyebrow: 'Your inbox', subtitle: 'Reminders and changes to tasks you can currently access.',
    unreadSummary: 'Unread notifications', unreadCaption: 'in tasks I can access',
    refresh: 'Refresh notifications', unreadOnly: 'Unread only', total: 'Currently accessible', unreadCount: 'Unread',
    empty: 'No notifications yet', emptyHelp: 'New notifications will appear here. You can keep working on your tasks.',
    emptyUnread: 'No unread notifications', emptyUnreadHelp: 'View all notifications or return to your tasks.',
    kinds: ['Task completed', 'Deadline reminder', 'Task overdue', 'New task'],
    read: 'Read', unread: 'Unread', mark: 'Mark as read', project: 'Project',
    connecting: 'Connecting live updates…', connected: 'Live updates connected.', reconnecting: 'Reconnecting…',
    offline: 'Live updates are offline. Refresh notifications to check their current state.', reconnect: 'Reconnect',
    cached: 'Previously loaded notifications are shown. Refresh when the connection recovers.',
    tasks: 'Go to tasks', anonymous: 'Sign in to view your notifications.',
  },
} as const;
export const copy = notificationsCopy.uk;

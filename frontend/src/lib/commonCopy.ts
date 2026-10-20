import type { Locale } from './i18n';

export const commonCopy = {
  uk: {
    language: 'Мова інтерфейсу', documentTitle: 'TaskManager — командні завдання', loading: 'Завантаження…', retry: 'Повторити',
    loadFailed: 'Не вдалося завантажити дані. Повторіть спробу.', trace: 'Код звернення',
    pagination: 'Сторінки результатів', results: '{total} результатів · Сторінка {page} з {last}', back: 'Назад', next: 'Далі',
    noDeadline: 'Без строку', chooseProject: 'Оберіть проєкт.', chooseAssignee: 'Оберіть виконавця.',
    titleRequired: 'Введіть назву.', titleLimit: 'Не більше 100 символів.', descriptionLimit: 'Не більше 1000 символів.',
    deadlineRequired: 'Для нагадування потрібен строк виконання.', minutesRange: 'Введіть ціле число від 1 до 10080.', invalidState: 'Оберіть підтримуваний статус.',
    skip: 'Перейти до вмісту', workspace: 'Робочий простір', overview: 'Огляд', tasks: 'Завдання', projects: 'Проєкти', team: 'Команда', teams: 'Команди', users: 'Користувачі',
    ai: 'AI нотатки', notifications: 'Сповіщення', navigation: 'Головна навігація', footerFirst: 'Ваші завдання.', footerSecond: 'Спільний результат.',
    profile: 'Профіль', logout: 'Вийти', loggingOut: 'Вихід…', connectionLost: 'З’єднання перервано', missingPage: 'Сторінку не знайдено', returnOverview: 'Повернутися до огляду',
  },
  en: {
    language: 'Interface language', documentTitle: 'TaskManager — team tasks', loading: 'Loading…', retry: 'Retry',
    loadFailed: 'The data could not be loaded. Please try again.', trace: 'Support reference',
    pagination: 'Result pages', results: '{total} results · Page {page} of {last}', back: 'Previous', next: 'Next',
    noDeadline: 'No deadline', chooseProject: 'Choose a project.', chooseAssignee: 'Choose an assignee.',
    titleRequired: 'Enter a title.', titleLimit: 'Use at most 100 characters.', descriptionLimit: 'Use at most 1000 characters.',
    deadlineRequired: 'A deadline is required for reminders.', minutesRange: 'Enter a whole number between 1 and 10080.', invalidState: 'Choose a supported status.',
    skip: 'Skip to content', workspace: 'Workspace', overview: 'Overview', tasks: 'Tasks', projects: 'Projects', team: 'Team', teams: 'Teams', users: 'Users',
    ai: 'AI notes', notifications: 'Notifications', navigation: 'Main navigation', footerFirst: 'Your tasks.', footerSecond: 'Shared results.',
    profile: 'Profile', logout: 'Sign out', loggingOut: 'Signing out…', connectionLost: 'Connection interrupted', missingPage: 'Page not found', returnOverview: 'Return to overview',
  },
} as const;

export const apiMessages = {
  uk: {
    0: 'Немає з’єднання із сервером. Перевірте мережу та повторіть запит.',
    400: 'Перевірте введені дані.', 401: 'Увійдіть у свій обліковий запис.', 403: 'Ви не маєте дозволу на цю дію.',
    404: 'Об’єкт недоступний або вже видалений.', 409: 'Дані або права змінилися. Оновіть запис і перевірте зміни.',
    410: 'Строк дії минув або посилання відкликано.', 428: 'Завантажте актуальну версію перед збереженням.',
    429: 'Забагато запитів. Зачекайте хвилину та повторіть спробу.', 502: 'Сервер повернув неочікувану відповідь. Повторіть запит.',
    503: 'Сервіс тимчасово недоступний. Спробуйте пізніше.', unknown: 'Не вдалося виконати запит. Спробуйте ще раз.',
  },
  en: {
    0: 'Cannot connect to the server. Check your connection and try again.',
    400: 'Check the entered information.', 401: 'Sign in to your account.', 403: 'You do not have permission to perform this action.',
    404: 'This item is unavailable or has been deleted.', 409: 'The data or permissions changed. Refresh the record and review the changes.',
    410: 'The item expired or the link was revoked.', 428: 'Load the current version before saving.',
    429: 'Too many requests. Wait a minute and try again.', 502: 'The server returned an unexpected response. Try again.',
    503: 'The service is temporarily unavailable. Try again later.', unknown: 'The request could not be completed. Please try again.',
  },
} as const;

export function localizedApiMessage(status: number, locale: Locale): string {
  const messages: Readonly<Record<string, string>> = apiMessages[locale];
  return messages[status] ?? apiMessages[locale].unknown;
}

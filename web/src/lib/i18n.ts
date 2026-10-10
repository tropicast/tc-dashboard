import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';

void i18n.use(initReactI18next).init({
  resources: {
    en: {
      translation: {
        dashboard: 'Dashboard',
        stations: 'Stations',
        account: 'Account',
        signOut: 'Sign out',
      },
    },
    fr: {
      translation: {
        dashboard: 'Tableau de bord',
        stations: 'Stations',
        account: 'Compte',
        signOut: 'Déconnexion',
      },
    },
  },
  lng: localStorage.getItem('language') ?? 'en',
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
});

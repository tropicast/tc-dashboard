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
        tenant: 'Workspace',
        language: 'Language',
        english: 'English',
        french: 'French',
        lightTheme: 'Light theme',
        darkTheme: 'Dark theme',
      },
    },
    fr: {
      translation: {
        dashboard: 'Tableau de bord',
        stations: 'Stations',
        account: 'Compte',
        signOut: 'Déconnexion',
        tenant: 'Espace de travail',
        language: 'Langue',
        english: 'Anglais',
        french: 'Français',
        lightTheme: 'Thème clair',
        darkTheme: 'Thème sombre',
      },
    },
  },
  lng: localStorage.getItem('language') ?? 'en',
  fallbackLng: 'en',
  debug: false,
  interpolation: { escapeValue: false },
});

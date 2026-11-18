import { createContext, useContext, useLayoutEffect, type ReactNode } from 'react';
import en from '../../lib/translations/en.json';
import fr from '../../lib/translations/fr.json';

export type Language = 'en' | 'fr';
export type LanguagePreference = 'auto' | Language;
export type TranslationKey = keyof typeof en;
type Parameters = Record<string, string | number>;

const catalog: Record<Language, typeof en> = { en, fr };
export function resolveLanguage(preference: LanguagePreference, deviceLanguage: string): Language {
  return preference === 'auto' ? deviceLanguage.split(/[-_]/, 1)[0].toLowerCase() === 'fr' ? 'fr' : 'en' : preference;
}

export function translate(language: Language, key: TranslationKey, parameters: Parameters = {}) {
  return (catalog[language]?.[key] ?? en[key]).replace(/\{(\w+)\}/g, (match, name: string) =>
    Object.hasOwn(parameters, name) ? String(parameters[name]) : match);
}

export type Translate = (key: TranslationKey, parameters?: Parameters) => string;
const translators: Record<Language, Translate> = {
  en: (key, parameters) => translate('en', key, parameters),
  fr: (key, parameters) => translate('fr', key, parameters),
};
const Strings = createContext<Translate>(translators.en);

export function LanguageProvider({ language, children }: { language: Language; children: ReactNode }) {
  useLayoutEffect(() => { document.documentElement.lang = language; }, [language]);
  return <Strings.Provider value={translators[language] ?? translators.en}>{children}</Strings.Provider>;
}

export const useStrings = () => useContext(Strings);

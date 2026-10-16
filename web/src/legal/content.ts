/**
 * Privacy Policy, Terms of Service and cookie notice (#94). DRAFTS: the owner must complete the bracketed operator
 * details and approve the texts (ideally with a lawyer) before setting `draft: false` in legal.json.
 *
 * When a text changes in substance, change `termsVersion` in legal.json and `Legal.TermsVersion` in the API (a test
 * keeps them equal): every user is then asked to accept the new version.
 */

export type LegalSection = { id?: string; heading: string; paragraphs?: string[]; list?: string[] };

export type LegalTexts = {
  privacy: LegalSection[];
  terms: LegalSection[];
  /** Purpose of each cookie and local storage entry in legal.json, by name. */
  storage: Record<string, string>;
  lifetimes: Record<string, string>;
};

/** Operator details: to be completed by the owner. */
const operator = {
  name: '[Operator legal name, legal form, registered address and registration number]',
  contact: '[privacy contact email]',
  law: 'French law',
};

const en: LegalTexts = {
  privacy: [
    {
      heading: 'Who we are',
      paragraphs: [
        `Tropicast (app.tropicastradio.com) is operated by ${operator.name}, the controller of the personal data described here. Contact: ${operator.contact}.`,
      ],
    },
    {
      heading: 'What we collect and why',
      list: [
        'Account: email address, password (stored only as a hash), sign-up date, the version of these terms you accepted and when. Purpose: to provide the service (contract).',
        'Workspaces: workspace names, members and their roles, invitations, stations and their public details, broadcast credentials (stored only as hashes). Purpose: to provide the service (contract).',
        'Security: sign-ins, failed sign-ins, desktop devices, password changes, with the IP address. Purpose: to protect your account (legitimate interest).',
        'Audit log: who changed stations, credentials, members and plans in a workspace. Purpose: accountability for the workspace (legitimate interest).',
        'Billing: plan, subscription status and the payment provider’s customer ID. Card details go directly to Stripe and never reach us. Purpose: contract and legal accounting obligations.',
        'Usage: listener counts, listening time and data transfer per station, without listener identities. Purpose: statistics and plan limits (contract).',
        'Technical logs: request logs and traces, with IP addresses, never passwords or tokens. Purpose: operation and security (legitimate interest).',
      ],
    },
    {
      heading: 'Listeners of stations',
      paragraphs: [
        'When someone listens to a station, our streaming servers log their IP address, the station and the player’s user agent. We use these logs to run the service, prevent abuse and count listeners. We do not build listener profiles, and listener data is never sold or shared with stations.',
      ],
    },
    {
      heading: 'Who receives the data',
      paragraphs: ['Only these processors, under data processing agreements:'],
      list: [
        'Hetzner Online (Germany): servers.',
        'Neon (database hosted in the EU, Frankfurt).',
        'Grafana Labs: logs, metrics and traces.',
        'Stripe: payments.',
        'Our email provider: account emails.',
        'Cloudflare: DNS.',
      ],
    },
    {
      heading: 'Transfers outside the EU',
      paragraphs: [
        'Some processors (Stripe, Grafana Labs, Cloudflare) are based in the United States. Transfers rely on the EU-US Data Privacy Framework or the European Commission’s standard contractual clauses.',
      ],
    },
    {
      heading: 'How long we keep it',
      list: [
        'Account and workspace data: until you delete your account or the workspace.',
        'Security events: 90 days.',
        'Audit log: as long as the workspace exists. When you delete your account, your name is removed from it.',
        'Technical and listener logs: about 14 days.',
        'Database backups: 30 days, so deleted data disappears from backups within 30 days.',
        'Billing records: as long as accounting law requires (10 years in France), at Stripe.',
      ],
    },
    {
      heading: 'Your rights',
      paragraphs: [
        'You can access, correct, export and delete your data, object to processing based on legitimate interest, and restrict processing. On the Account page you can download all your data (Download my data) and delete your account (Delete account). A workspace Owner can also delete the whole workspace, with its stations, uploaded audio and history (Delete workspace). For anything else, write to us at the contact address above. You can also complain to your data protection authority; in France, the CNIL (www.cnil.fr).',
      ],
    },
    {
      id: 'cookies',
      heading: 'Cookies and local storage',
      paragraphs: [
        'We only use cookies and browser storage that the dashboard needs to work. They are exempt from consent, so there is no cookie banner. We use no analytics, advertising or third-party cookies. Public station pages and the embeddable player use none at all.',
      ],
    },
  ],
  terms: [
    {
      heading: 'The service',
      paragraphs: [
        `Tropicast, operated by ${operator.name}, lets you run internet radio stations: a dashboard, a desktop broadcasting app, streaming servers, public station pages and an embeddable player. By creating an account you accept these terms and the Privacy Policy.`,
      ],
    },
    {
      heading: 'Your account',
      paragraphs: [
        'Keep your password and broadcast credentials secret. You are responsible for what happens in your workspaces, including what your members do. You must be at least 16 years old, or of the age of digital consent in your country.',
      ],
    },
    {
      heading: 'Plans and payment',
      paragraphs: [
        'Plans and their limits (stations, listeners, bitrate, formats) are shown in the dashboard. Paid plans are billed monthly in advance through Stripe and renew automatically. You can cancel at any time in the billing portal; the plan stays until the end of the paid period, and periods already paid are not refunded, except where the law requires it. If a payment fails, the paid plan stays for 7 days, then the workspace returns to the Free plan.',
      ],
    },
    {
      heading: 'What you broadcast',
      paragraphs: [
        'You are solely responsible for the content you broadcast and publish, and for holding every right and licence it needs, including music royalties (e.g. SACEM in France). You must not broadcast content that is illegal, infringes rights, incites hatred or violence, or is sexually explicit involving minors. We may suspend a station or workspace that breaks these rules or the law, and act on valid notices from rights holders.',
      ],
    },
    {
      heading: 'Availability',
      paragraphs: [
        'We work to keep the service available but do not guarantee uninterrupted service unless a separate agreement says so. We may change or interrupt the service for maintenance, security or legal reasons.',
      ],
    },
    {
      heading: 'Liability',
      paragraphs: [
        'To the extent the law allows, we are not liable for indirect damages, lost revenue or lost audience. Our total liability is limited to the amounts you paid us in the 12 months before the event. Nothing limits liability that cannot be limited by law.',
      ],
    },
    {
      heading: 'Ending',
      paragraphs: [
        'You can delete your account at any time from the Account page. We may close an account that seriously or repeatedly breaks these terms, after notice except in urgent cases.',
      ],
    },
    {
      heading: 'Changes and law',
      paragraphs: [
        `We will tell you about substantial changes to these terms and ask you to accept them in the dashboard. These terms are governed by ${operator.law}. Consumers keep the protection of the law of their country of residence.`,
      ],
    },
  ],
  storage: {
    '__Host-tropicast': 'Keeps you signed in to the dashboard (session).',
    '__Host-tropicast-af': 'Protects forms against cross-site request forgery.',
    'XSRF-TOKEN': 'Protects forms against cross-site request forgery (read by the dashboard).',
    'tenant-id': 'Remembers the workspace you selected.',
    language: 'Remembers the language you chose.',
    theme: 'Remembers the light or dark theme you chose.',
  },
  lifetimes: {
    '14d': '14 days, renewed while you use the dashboard',
    session: 'Until the browser closes',
    persistent: 'Until you clear it',
  },
};

const fr: LegalTexts = {
  privacy: [
    {
      heading: 'Qui nous sommes',
      paragraphs: [
        `Tropicast (app.tropicastradio.com) est exploité par ${operator.name}, responsable du traitement des données personnelles décrites ici. Contact : ${operator.contact}.`,
      ],
    },
    {
      heading: 'Ce que nous collectons et pourquoi',
      list: [
        'Compte : adresse e-mail, mot de passe (conservé uniquement sous forme d’empreinte), date d’inscription, version des présentes conditions acceptée et date d’acceptation. Finalité : fournir le service (contrat).',
        'Espaces de travail : noms des espaces, membres et rôles, invitations, stations et leurs informations publiques, identifiants de diffusion (conservés uniquement sous forme d’empreinte). Finalité : fournir le service (contrat).',
        'Sécurité : connexions, échecs de connexion, appareils de bureau, changements de mot de passe, avec l’adresse IP. Finalité : protéger votre compte (intérêt légitime).',
        'Journal d’audit : qui a modifié les stations, identifiants, membres et offres d’un espace. Finalité : traçabilité de l’espace (intérêt légitime).',
        'Facturation : offre, état de l’abonnement et identifiant client du prestataire de paiement. Les données de carte vont directement à Stripe et ne nous parviennent jamais. Finalité : contrat et obligations comptables.',
        'Utilisation : nombre d’auditeurs, temps d’écoute et volume de données par station, sans identité des auditeurs. Finalité : statistiques et limites de l’offre (contrat).',
        'Journaux techniques : journaux de requêtes et traces, avec les adresses IP, jamais les mots de passe ni les jetons. Finalité : exploitation et sécurité (intérêt légitime).',
      ],
    },
    {
      heading: 'Auditeurs des stations',
      paragraphs: [
        'Quand quelqu’un écoute une station, nos serveurs de diffusion enregistrent son adresse IP, la station et l’agent utilisateur du lecteur. Ces journaux servent à faire fonctionner le service, à prévenir les abus et à compter les auditeurs. Nous ne créons pas de profils d’auditeurs, et leurs données ne sont jamais vendues ni transmises aux stations.',
      ],
    },
    {
      heading: 'Destinataires',
      paragraphs: ['Uniquement ces sous-traitants, liés par un contrat de sous-traitance :'],
      list: [
        'Hetzner Online (Allemagne) : serveurs.',
        'Neon (base de données hébergée dans l’UE, à Francfort).',
        'Grafana Labs : journaux, métriques et traces.',
        'Stripe : paiements.',
        'Notre prestataire d’e-mail : e-mails liés au compte.',
        'Cloudflare : DNS.',
      ],
    },
    {
      heading: 'Transferts hors de l’UE',
      paragraphs: [
        'Certains sous-traitants (Stripe, Grafana Labs, Cloudflare) sont établis aux États-Unis. Ces transferts reposent sur le cadre de protection des données UE-États-Unis ou sur les clauses contractuelles types de la Commission européenne.',
      ],
    },
    {
      heading: 'Durées de conservation',
      list: [
        'Données du compte et des espaces : jusqu’à la suppression du compte ou de l’espace.',
        'Événements de sécurité : 90 jours.',
        'Journal d’audit : tant que l’espace existe. À la suppression de votre compte, votre nom en est retiré.',
        'Journaux techniques et d’écoute : environ 14 jours.',
        'Sauvegardes de la base : 30 jours ; les données supprimées disparaissent donc des sauvegardes sous 30 jours.',
        'Pièces comptables : la durée imposée par la loi (10 ans en France), chez Stripe.',
      ],
    },
    {
      heading: 'Vos droits',
      paragraphs: [
        'Vous pouvez accéder à vos données, les rectifier, les exporter et les effacer, vous opposer aux traitements fondés sur l’intérêt légitime et en demander la limitation. Sur la page Compte, vous pouvez télécharger toutes vos données (Télécharger mes données) et supprimer votre compte (Supprimer le compte). Le propriétaire d’un espace peut aussi supprimer l’espace entier, avec ses stations, son audio envoyé et son historique (Supprimer l’espace). Pour toute autre demande, écrivez-nous à l’adresse ci-dessus. Vous pouvez aussi saisir l’autorité de protection des données ; en France, la CNIL (www.cnil.fr).',
      ],
    },
    {
      id: 'cookies',
      heading: 'Cookies et stockage local',
      paragraphs: [
        'Nous n’utilisons que les cookies et le stockage du navigateur nécessaires au fonctionnement du tableau de bord. Ils sont exemptés de consentement : il n’y a donc pas de bandeau cookies. Aucun cookie de mesure d’audience, de publicité ou de tiers. Les pages publiques des stations et le lecteur intégrable n’en utilisent aucun.',
      ],
    },
  ],
  terms: [
    {
      heading: 'Le service',
      paragraphs: [
        `Tropicast, exploité par ${operator.name}, permet de gérer des radios en ligne : un tableau de bord, une application de diffusion, des serveurs de diffusion, des pages publiques et un lecteur intégrable. En créant un compte, vous acceptez les présentes conditions et la Politique de confidentialité.`,
      ],
    },
    {
      heading: 'Votre compte',
      paragraphs: [
        'Gardez secrets votre mot de passe et vos identifiants de diffusion. Vous êtes responsable de ce qui se passe dans vos espaces, y compris des actions de vos membres. Vous devez avoir au moins 16 ans, ou l’âge du consentement numérique de votre pays.',
      ],
    },
    {
      heading: 'Offres et paiement',
      paragraphs: [
        'Les offres et leurs limites (stations, auditeurs, débit, formats) sont indiquées dans le tableau de bord. Les offres payantes sont facturées mensuellement à l’avance via Stripe et se renouvellent automatiquement. Vous pouvez résilier à tout moment depuis le portail de facturation ; l’offre reste active jusqu’à la fin de la période payée, et les périodes payées ne sont pas remboursées, sauf obligation légale. En cas d’échec de paiement, l’offre payante reste active 7 jours, puis l’espace repasse à l’offre gratuite.',
      ],
    },
    {
      heading: 'Ce que vous diffusez',
      paragraphs: [
        'Vous êtes seul responsable des contenus que vous diffusez et publiez, et de détenir tous les droits et licences nécessaires, y compris les droits musicaux (par exemple la SACEM en France). Vous ne devez pas diffuser de contenus illicites, contrefaisants, incitant à la haine ou à la violence, ou à caractère sexuel impliquant des mineurs. Nous pouvons suspendre une station ou un espace qui enfreint ces règles ou la loi, et donner suite aux notifications valables des ayants droit.',
      ],
    },
    {
      heading: 'Disponibilité',
      paragraphs: [
        'Nous nous efforçons de maintenir le service disponible, sans garantir un service ininterrompu sauf accord distinct. Nous pouvons modifier ou interrompre le service pour maintenance, sécurité ou raisons légales.',
      ],
    },
    {
      heading: 'Responsabilité',
      paragraphs: [
        'Dans la mesure permise par la loi, nous ne sommes pas responsables des dommages indirects, pertes de revenus ou d’audience. Notre responsabilité totale est limitée aux sommes que vous nous avez versées au cours des 12 mois précédant le fait générateur. Rien ne limite une responsabilité que la loi interdit de limiter.',
      ],
    },
    {
      heading: 'Fin du contrat',
      paragraphs: [
        'Vous pouvez supprimer votre compte à tout moment depuis la page Compte. Nous pouvons fermer un compte qui enfreint gravement ou de façon répétée les présentes conditions, après préavis sauf urgence.',
      ],
    },
    {
      heading: 'Modifications et droit applicable',
      paragraphs: [
        'Nous vous informerons de toute modification substantielle des présentes conditions et vous demanderons de l’accepter dans le tableau de bord. Les présentes conditions sont régies par le droit français. Les consommateurs conservent la protection de la loi de leur pays de résidence.',
      ],
    },
  ],
  storage: {
    '__Host-tropicast': 'Vous garde connecté au tableau de bord (session).',
    '__Host-tropicast-af':
      'Protège les formulaires contre la falsification de requêtes intersites.',
    'XSRF-TOKEN':
      'Protège les formulaires contre la falsification de requêtes intersites (lu par le tableau de bord).',
    'tenant-id': 'Mémorise l’espace de travail sélectionné.',
    language: 'Mémorise la langue choisie.',
    theme: 'Mémorise le thème clair ou sombre choisi.',
  },
  lifetimes: {
    '14d': '14 jours, renouvelés tant que vous utilisez le tableau de bord',
    session: 'Jusqu’à la fermeture du navigateur',
    persistent: 'Jusqu’à ce que vous l’effaciez',
  },
};

export const legalTexts = { en, fr };

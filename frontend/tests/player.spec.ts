import { test, expect } from '@playwright/test';

const fieldFocusStroke = /^rgba?\([^)]+\) 0px 0px 0px 1\.5px inset$/;

test.beforeEach(async ({ page }) => { await page.goto('/?preview=1'); });

test('minimal player keeps four equal controls above a proportional bottom progress bar', async ({ page }) => {
  await page.setViewportSize({ width: 350, height: 52 });
  const progress = page.getByRole('slider', { name: 'Position dans le morceau' });
  await expect(progress).toBeInViewport({ ratio: 1 });
  await expect(page.locator('.track-time')).toHaveCount(0);
  await expect(page.locator('.mini-title')).toHaveCSS('font-size', '12px');
  const bounds = (await progress.boundingBox())!;
  expect(bounds.y).toBeGreaterThan(34);
  const fill = () => page.locator('.playback-progress-fill').evaluate(bar => new DOMMatrix(getComputedStyle(bar).transform).a);
  await progress.fill('101.5');
  await expect.poll(fill).toBeCloseTo(0.5, 3);
  await progress.fill('203');
  await expect.poll(fill).toBeCloseTo(1, 3);
  const rail = (await page.locator('.playback-progress-fill').boundingBox())!;
  const card = (await page.locator('.player-mini').boundingBox())!;
  expect(rail.x).toBeCloseTo(card.x, 2);
  expect(rail.width).toBeCloseTo(card.width, 2);
  expect(rail.y + rail.height).toBeCloseTo(card.y + card.height, 2);
  await progress.click({ position: { x: bounds.width / 2, y: bounds.height / 2 } });
  await expect.poll(fill).toBeCloseTo(0.5, 2);
  const items = await page.locator('.track-row > *').evaluateAll(elements => elements.map(element => {
    const { top, height } = element.getBoundingClientRect();
    return top + height / 2;
  }));
  expect(Math.max(...items) - Math.min(...items)).toBeLessThan(1);
  const sound = page.getByRole('button', { name: 'Régler le volume' });
  const sizes = await page.locator('.player-mini button').evaluateAll(buttons => buttons.map(button => {
    const { width, height } = button.getBoundingClientRect();
    return [width, height];
  }));
  expect(sizes).toEqual(Array.from({ length: 4 }, () => [28, 28]));
  await sound.hover();
  await expect(page.getByRole('slider')).toHaveCount(1);
  await sound.click();
  await page.setViewportSize({ width: 350, height: 160 });
  const popover = page.getByRole('dialog', { name: 'Régler le volume' });
  await expect(popover).toBeInViewport();
  const volume = popover.getByRole('slider', { name: 'Volume', exact: true });
  await volume.fill('63');
  await expect(popover.locator('.volume-control > output')).toHaveText('63%');
  await popover.getByRole('button', { name: 'Couper le son' }).click();
  await expect(popover.getByRole('button', { name: 'Activer le son' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(popover).toHaveCount(0);
  await expect(sound).toBeFocused();
  const original = await page.locator('.mini-title').textContent();
  await page.getByRole('button', { name: 'Morceau suivant' }).click();
  await expect(page.locator('.mini-title')).not.toHaveText(original!);
});

test('minimal progress moves between playback samples when the widget is passive', async ({ page }) => {
  await page.addInitScript(() => {
    const bridge = new EventTarget();
    const publish = (data: object) => bridge.dispatchEvent(new MessageEvent('message', { data: { type: 'state', ...data } }));
    Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: Object.assign(bridge, {
      postMessage: (message: { command: string }) => {
        if (message.command === 'ready') publish({ position: 2, duration: 10, isPlaying: true, interactive: false });
      },
    }) } });
    window.addEventListener('playback-sample', event => publish((event as CustomEvent).detail));
  });
  await page.setViewportSize({ width: 350, height: 52 });
  await page.goto('/');
  const bar = page.locator('.playback-progress-fill');
  const fraction = () => bar.evaluate(node => new DOMMatrix(getComputedStyle(node).transform).a);
  await expect(page.locator('main')).toHaveAttribute('data-passive', 'true');
  await expect.poll(fraction).toBeGreaterThan(0.23);
  await page.evaluate(() => window.dispatchEvent(new CustomEvent('playback-sample', { detail: { position: 5, isPlaying: false } })));
  await expect.poll(fraction).toBeCloseTo(0.5, 3);
  await expect.poll(() => bar.evaluate(node => node.getAnimations().length)).toBe(0);
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.evaluate(() => window.dispatchEvent(new CustomEvent('playback-sample', { detail: { position: 6, isPlaying: true } })));
  await expect.poll(() => bar.evaluate(node => node.getAnimations().length)).toBe(0);
  await expect.poll(fraction).toBeCloseTo(0.6, 3);
});

test('the title tooltip is plain text inside the slim player', async ({ page }) => {
  await page.setViewportSize({ width: 350, height: 52 });
  const title = page.locator('.mini-title');
  await expect(title).not.toHaveAttribute('title');
  await title.hover();
  const tip = page.getByRole('tooltip');
  await expect(tip).toHaveText(/Midnight Drive/);
  await expect(tip).toBeInViewport({ ratio: 1 });
  const bounds = (await tip.boundingBox())!;
  expect(bounds.y + bounds.height).toBeLessThan((await page.locator('.playback-progress').boundingBox())!.y);
  await expect(tip).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
  await expect(tip).toHaveCSS('border-top-width', '0px');
  await page.keyboard.press('Escape');
  await expect(tip).toHaveCount(0);
});

test('an overflowing title travels inside its slot and respects reduced motion', async ({ page }) => {
  await page.setViewportSize({ width: 255, height: 52 });
  const title = page.locator('.mini-title');
  const label = title.locator('span');
  await expect(title).toHaveAttribute('data-scroll-title', '');
  await expect(page.getByRole('button', { name: 'Options du lecteur' })).toBeInViewport({ ratio: 1 });
  await expect(label).toHaveCSS('animation-name', 'mini-title-pan');
  expect(await label.evaluate(element => {
    const [animation] = element.getAnimations();
    animation.currentTime = Number(animation.effect?.getTiming().duration);
    return new DOMMatrix(getComputedStyle(element).transform).m41;
  })).toBeLessThan(-10);
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await expect(label).toHaveCSS('animation-name', 'none');
});

test('right click, switches, glass choices and compact layout work', async ({ page }) => {
  await page.locator('.track-title').click({ button: 'right' });
  await page.getByRole('menuitem', { name: /Apparence et écoute/ }).click();
  const motion = page.getByRole('switch', { name: /^Animations réduites/ });
  await motion.click();
  await expect(motion).toBeChecked();
  await page.getByRole('button', { name: 'Position du lecteur', exact: true }).click();
  await page.getByRole('menuitem', { name: 'En bas à gauche', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Position du lecteur', exact: true })).toContainText('En bas à gauche');
  await expect(page.locator('select')).toHaveCount(0);
  await page.getByRole('button', { name: 'Compact', exact: true }).click();
  await page.getByRole('button', { name: 'Fermer le panneau' }).click();
  await page.setViewportSize({ width: 400, height: 260 });
  await expect(page.getByRole('button', { name: 'Morceau précédent', exact: true })).toBeInViewport();
  await expect(page.getByRole('button', { name: 'Bibliothèque', exact: true })).toBeInViewport();
  await expect(page.locator('.track-meta .track-time')).toHaveText(/^\d+:\d{2} \/ \d+:\d{2}$/);
  await expect(page.locator('.player-compact .track-title')).toHaveCSS('font-size', '14px');
  await expect(page.locator('.seek-slider input')).toBeInViewport();
  await expect(page.locator('.playback-progress')).toHaveCount(0);
  await page.getByRole('button', { name: 'Options du lecteur' }).click();
  await expect(page.getByRole('menuitem', { name: 'Masquer le widget' })).toBeInViewport({ ratio: 1 });
});

test('settings fill the panel and offer all six Minimalist anchors', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.getByRole('button', { name: 'Options du lecteur' }).click();
  await page.getByRole('menuitem', { name: 'Apparence et écoute' }).click();
  for (const name of ['Disposition du lecteur', 'Mode de l’aura']) {
    const group = page.getByRole('group', { name });
    const widths = await group.evaluate(element => {
      const [first, second] = [...element.querySelectorAll('button')].map(button => button.getBoundingClientRect());
      const style = getComputedStyle(element);
      const innerRight = element.getBoundingClientRect().right - parseFloat(style.borderRightWidth) - parseFloat(style.paddingRight);
      return { first: first.width, second: second.width, rightGap: innerRight - second.right };
    });
    expect(widths.first).toBeCloseTo(widths.second, 0);
    expect(Math.abs(widths.rightGap)).toBeLessThan(1);
  }

  const trigger = page.getByRole('button', { name: 'Position du lecteur', exact: true });
  await expect(trigger).toHaveText('En haut au centre');
  await trigger.click();
  const menu = page.getByRole('menu', { name: 'Position du lecteur' });
  await expect(menu.getByRole('menuitem')).toHaveText([
    'En haut à gauche', 'En haut au centre', 'En haut à droite',
    'En bas à gauche', 'En bas au centre', 'En bas à droite',
  ]);
  await expect(menu.getByRole('menuitem', { name: 'En haut au centre' })).toHaveAttribute('aria-current', 'true');
  const surface = menu.locator('..');
  for (const width of [420, 380]) {
    await page.setViewportSize({ width, height: 600 });
    await expect.poll(async () => {
      const field = (await trigger.boundingBox())!;
      const popup = (await surface.boundingBox())!;
      return Math.abs(popup.x - field.x) + Math.abs(popup.width - field.width);
    }).toBeLessThan(1);
  }
  await menu.getByRole('menuitem', { name: 'En bas à droite' }).click();
  await expect(trigger).toHaveText('En bas à droite');
});

test('library supports search and cancellable glass confirmation', async ({ page }) => {
  await page.getByRole('button', { name: 'Options du lecteur' }).click();
  await page.getByRole('menuitem', { name: 'Bibliothèque', exact: true }).click();
  await expect(page.locator('.track-item')).toHaveCount(3);
  const field = await page.getByRole('searchbox').boundingBox();
  const firstRow = await page.locator('.track-item').first().boundingBox();
  expect(firstRow!.y - (field!.y + field!.height)).toBeGreaterThanOrEqual(14);
  await expect(page.locator('.ogui-search > i')).toHaveCSS('width', '17px');
  const searchField = page.getByRole('searchbox');
  await searchField.fill('Ocean');
  const searchControl = page.locator('.library-pane .ogui-search');
  await expect(searchControl).toHaveCSS('box-shadow', fieldFocusStroke);
  await searchField.press('Tab');
  await expect(page.getByRole('button', { name: 'Effacer la recherche' })).toBeFocused();
  await expect(searchControl).toHaveCSS('box-shadow', 'none');
  await expect(page.locator('.track-item')).toHaveCount(1);
  await page.getByRole('button', { name: /Options de Ocean/ }).click();
  await page.getByRole('menuitem', { name: 'Supprimer le fichier' }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.getByRole('button', { name: 'Annuler', exact: true }).click();
  await expect(page.locator('.track-item')).toHaveCount(1);
});

test('a song already in a playlist offers removal while the library copy stays', async ({ page }) => {
  await page.getByRole('button', { name: 'Options du lecteur' }).click();
  await page.getByRole('menuitem', { name: 'Bibliothèque', exact: true }).click();
  await page.getByRole('button', { name: 'Favoris', exact: true }).click();
  const options = page.getByRole('button', { name: /Options de Midnight Drive/ });
  await options.click();
  await expect(page.getByRole('menuitem', { name: 'Ajouter à « Favoris »' })).toHaveCount(0);
  await expect(page.getByRole('menuitem', { name: 'Supprimer le fichier' })).toBeVisible();
  await page.getByRole('menuitem', { name: 'Retirer de « Favoris »' }).click();
  await expect(options).toHaveCount(0);
  await page.getByRole('button', { name: 'Tous', exact: true }).click();
  await options.click();
  await page.getByRole('menuitem', { name: 'Ajouter à « Favoris »' }).click();
  await options.click();
  await expect(page.getByRole('menuitem', { name: 'Retirer de « Favoris »' })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'Ajouter à « Favoris »' })).toHaveCount(0);
});

test('an empty playlist can add tracks from the library in one batch', async ({ page }) => {
  await page.getByRole('button', { name: 'Options du lecteur' }).click();
  await page.getByRole('menuitem', { name: 'Bibliothèque', exact: true }).click();
  await page.getByRole('button', { name: 'Nouvelle playlist', exact: true }).click();
  await page.getByRole('textbox', { name: 'Nom de la playlist' }).fill('Sélection');
  await page.getByRole('button', { name: 'Créer', exact: true }).click();
  const openPicker = page.getByRole('button', { name: 'Ajouter depuis la bibliothèque' });
  const midnight = page.getByRole('button', { name: /Sélectionner Midnight Drive/ });
  const golden = page.getByRole('button', { name: /Sélectionner Golden Hour/ });
  await openPicker.click();
  await expect(page.locator('.playlist-picker-row')).toHaveCount(3);
  await midnight.click();
  await page.getByRole('button', { name: 'Retour à la bibliothèque' }).click();
  await expect(page.locator('.track-item')).toHaveCount(0);
  await openPicker.click();
  await expect(midnight).toHaveAttribute('aria-pressed', 'false');
  await midnight.click();
  await expect(midnight).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('searchbox').fill('Golden');
  await expect(page.locator('.playlist-picker-row')).toHaveCount(1);
  await golden.click();
  await expect(page.getByText('2 morceaux sélectionnés')).toBeVisible();
  await page.getByRole('button', { name: 'Ajouter à la playlist' }).click();
  await expect(page.locator('.track-item')).toHaveCount(2);
  const commands = await page.evaluate(() => window.__musicCommands?.filter(item => item.command === 'addPlaylistTracks'));
  expect(commands).toEqual([{ command: 'addPlaylistTracks', name: 'Sélection', value: ['Midnight Drive.wav', 'Golden Hour.wav'] }]);
});

test('the same download progress follows search and link tabs using byte counts', async ({ page }) => {
  await page.addInitScript(() => {
    const bridge = new EventTarget();
    const webview = Object.assign(bridge, {
      postMessage: (message: { command: string }) => {
        if (message.command === 'ready') bridge.dispatchEvent(new MessageEvent('message', { data: { type: 'state', language: 'fr', pane: 'search' } }));
      },
    });
    Object.defineProperty(window, 'chrome', { configurable: true, value: { webview } });
  });
  await page.goto('/');
  const sendProgress = (status: string, received: number, total: number, busy = true, succeeded = false) =>
    page.evaluate(download => (window.chrome?.webview as unknown as EventTarget).dispatchEvent(new MessageEvent('message', { data: { type: 'state', download } })),
      { status, received, total, busy, succeeded, error: '' });
  await expect(page.getByRole('tab', { name: 'Rechercher' })).toBeVisible();
  await sendProgress('Initialisation…', 0, 0);
  const bar = page.getByRole('progressbar');
  await expect(bar).toBeVisible();
  await expect(bar).not.toHaveAttribute('value', /./);
  await sendProgress('Téléchargement en cours…', 2500000, 10000000);
  await expect(bar).toHaveAttribute('value', '25');
  await page.getByRole('tab', { name: 'Lien ou fichier' }).click();
  await sendProgress('Téléchargement en cours…', 7500000, 10000000);
  await expect(bar).toHaveAttribute('value', '75');
  await expect(page.locator('.download-bytes')).toHaveText(/7,5\s*Mo\s*\/\s*10\s*Mo/);
  await sendProgress('Conversion audio…', 10000000, 10000000);
  await expect(bar).toHaveAttribute('value', '100');
  await sendProgress('Morceau ajouté', 0, 0, false, true);
  await expect(bar).toHaveCount(0);
  await expect(page.getByText('Morceau ajouté', { exact: true })).toBeVisible();
});

test('online tools can be enabled without blocking local file import', async ({ page }) => {
  await page.addInitScript(() => {
    const bridge = new EventTarget();
    const onlineTools = { available: false, complete: false, installing: false, failed: false, canInstall: true };
    const publish = (state: object) => bridge.dispatchEvent(new MessageEvent('message', { data: { type: 'state', ...state } }));
    const webview = Object.assign(bridge, {
      postMessage(message: { command: string }) {
        if (message.command === 'ready') publish({ language: 'en', pane: 'search', onlineTools });
        if (message.command === 'enableOnlineTools') publish({ onlineTools: { ...onlineTools, installing: true } });
      },
    });
    addEventListener('online-tools-failed', () => publish({ onlineTools: { ...onlineTools, failed: true } }));
    addEventListener('online-tools-installed', () => publish({ onlineTools: { ...onlineTools, available: true, complete: true } }));
    Object.defineProperty(window, 'chrome', { configurable: true, value: { webview } });
  });
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Enable online tools' })).toBeVisible();
  await expect(page.getByRole('searchbox')).toHaveCount(0);
  await page.getByRole('tab', { name: 'Link or file' }).click();
  const localFiles = page.getByRole('button', { name: 'Choose audio files' });
  await expect(localFiles).toBeEnabled();
  await page.getByRole('button', { name: 'Enable online tools' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Downloading and verifying online tools' })).toBeVisible();
  await expect(localFiles).toBeEnabled();
  await page.evaluate(() => dispatchEvent(new Event('online-tools-failed')));
  await expect(page.getByRole('alert')).toContainText('Installation failed');
  await page.getByRole('button', { name: 'Enable online tools' }).click();
  await page.evaluate(() => dispatchEvent(new Event('online-tools-installed')));
  await expect(page.getByRole('button', { name: 'Enable online tools' })).toHaveCount(0);
  await expect(page.getByRole('textbox', { name: /YouTube or Spotify/ })).toBeEnabled();
  await page.getByRole('tab', { name: 'Search', exact: true }).click();
  await expect(page.getByRole('searchbox')).toBeEnabled();
});

test('online search offers pagination and routes a selected result to the downloader', async ({ page }) => {
  await page.route('https://i.ytimg.com/**', route => route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="120" height="90"/>' }));
  await page.getByRole('button', { name: 'Options du lecteur' }).click();
  await page.getByRole('menuitem', { name: 'Bibliothèque', exact: true }).click();
  await page.getByRole('button', { name: 'Ajouter de la musique', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Ajouter de la musique', exact: true })).toBeVisible();
  await page.getByRole('searchbox', { name: 'Morceau ou artiste' }).fill('Nujabes');
  const form = page.locator('.online-form');
  const search = form.locator('.ogui-search');
  await expect(search).toHaveCSS('box-shadow', fieldFocusStroke);
  expect((await search.boundingBox())!.y - (await form.boundingBox())!.y).toBeGreaterThanOrEqual(7);
  await page.getByRole('button', { name: 'Rechercher', exact: true }).click();
  await expect(page.locator('.search-result')).toHaveCount(8);
  const artwork = page.locator('.result-artwork img').first();
  await expect(artwork).toHaveAttribute('loading', 'lazy');
  await expect(artwork).toHaveAttribute('decoding', 'async');
  await expect(artwork).toHaveAttribute('src', 'https://i.ytimg.com/vi/preview-1-0/default.jpg');
  await page.getByRole('button', { name: 'Écouter Nujabes · Session 1', exact: true }).click();
  await expect(page.getByText('Lecture en cours')).toBeVisible();
  await page.getByRole('button', { name: 'Arrêter la lecture de Nujabes · Session 1', exact: true }).click();
  await expect(page.getByText('Lecture en cours')).toHaveCount(0);
  await page.getByRole('button', { name: 'Écouter Nujabes · Session 1', exact: true }).click();
  await page.getByRole('button', { name: 'Suivante', exact: true }).click();
  await expect(page.getByText('Page 2', { exact: true })).toBeVisible();
  await expect(page.getByText('Lecture en cours')).toHaveCount(0);
  await expect(page.locator('.result-copy').first()).toContainText('Session 9');
  await page.getByRole('button', { name: /Ajouter Nujabes/ }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Choisir la destination' });
  const target = dialog.getByRole('button', { name: 'Enregistrer dans' });
  await expect(target).toHaveText('Tous');
  await dialog.getByRole('button', { name: 'Annuler' }).click();
  expect(await page.evaluate(() => window.__musicCommands?.filter(item => item.command === 'download'))).toEqual([]);
  await page.getByRole('button', { name: /Ajouter Nujabes/ }).first().click();
  await expect(dialog.getByRole('button', { name: 'Close Choisir la destination' })).toBeFocused();
  await target.press('ArrowDown');
  const destinations = page.getByRole('menu', { name: 'Enregistrer dans' });
  const all = destinations.getByRole('menuitem', { name: 'Tous' });
  await expect(all).toBeFocused();
  await expect(all).toHaveAttribute('aria-current', 'true');
  await all.press('Escape');
  await expect(destinations).toHaveCount(0);
  await expect(target).toBeFocused();
  await target.press('ArrowDown');
  await expect(all).toBeFocused();
  await all.press('ArrowDown');
  const favorites = destinations.getByRole('menuitem', { name: 'Favoris' });
  await expect(favorites).toBeFocused();
  await favorites.press('Enter');
  await expect(target).toHaveText('Favoris');
  expect(await page.evaluate(() => window.__musicCommands?.filter(item => item.command === 'download'))).toEqual([]);
  await dialog.getByRole('button', { name: 'Télécharger', exact: true }).click();
  const command = await page.evaluate(() => window.__musicCommands?.filter(item => item.command === 'download').at(-1));
  expect(command?.url).toContain('youtube.com/watch?v=preview2');
  expect(command?.name).toBe('Favoris');
});

test('custom playlist order changes by dragging or using the handle arrows', async ({ page }) => {
  await page.getByRole('button', { name: 'Options du lecteur' }).click();
  await page.getByRole('menuitem', { name: 'Bibliothèque', exact: true }).click();
  await expect(page.locator('.track-grip')).toHaveCount(0);
  await page.getByRole('button', { name: 'Favoris', exact: true }).click();
  const last = page.locator('.track-item').last();
  const height = (await last.boundingBox())!.height;
  await page.getByRole('button', { name: /Déplacer Midnight Drive/ }).dragTo(last, { targetPosition: { x: 100, y: height - 8 } });
  await expect(page.locator('.track-item').last()).toContainText('Midnight Drive');
  await page.getByRole('button', { name: /Déplacer Midnight Drive/ }).press('ArrowUp');
  await expect(page.locator('.track-item').nth(1)).toContainText('Midnight Drive');
});

test('URL input has one focus ring', async ({ page }) => {
  await page.getByRole('button', { name: 'Options du lecteur' }).click();
  await page.getByRole('menuitem', { name: 'Ajouter de la musique', exact: true }).click();
  const searchTab = page.getByRole('tab', { name: 'Rechercher', exact: true });
  await expect(searchTab).toHaveAttribute('aria-selected', 'true');
  const tabRail = (await page.getByRole('tablist', { name: 'Sources musicales' }).boundingBox())!;
  const paneBody = (await page.locator('.add-music-tabs').boundingBox())!;
  expect(Math.abs(tabRail.width - paneBody.width)).toBeLessThan(1);
  const tabWidths = await page.getByRole('tab').evaluateAll(tabs => tabs.map(tab => tab.getBoundingClientRect().width));
  expect(Math.abs(tabWidths[0] - tabWidths[1])).toBeLessThan(1);
  await page.getByRole('searchbox', { name: 'Morceau ou artiste' }).fill('Un titre');
  await page.getByRole('tab', { name: 'Lien ou fichier', exact: true }).click();
  await expect(page.getByRole('button', { name: /Choisir des fichiers audio/ })).toBeVisible();
  const field = page.getByRole('textbox', { name: 'Lien YouTube ou Spotify' });
  await field.fill('https://youtu.be/fixture');
  await searchTab.click();
  await expect(page.getByRole('searchbox', { name: 'Morceau ou artiste' })).toHaveValue('Un titre');
  await searchTab.press('ArrowRight');
  await expect(field).toHaveValue('https://youtu.be/fixture');
  await field.focus();
  await expect(field).toHaveCSS('box-shadow', fieldFocusStroke);
  await page.getByRole('button', { name: 'Ajouter à la bibliothèque', exact: true }).click();
  const destination = page.getByRole('dialog', { name: 'Choisir la destination' });
  await expect(destination.getByRole('button', { name: 'Enregistrer dans' })).toHaveText('Tous');
  await destination.getByRole('button', { name: 'Télécharger', exact: true }).click();
  const command = await page.evaluate(() => window.__musicCommands?.find(item => item.command === 'download'));
  expect(command).toEqual({ command: 'download', url: 'https://youtu.be/fixture' });
});

test('panels provide direct return paths to library and player', async ({ page }) => {
  await page.getByRole('button', { name: 'Options du lecteur' }).click();
  await page.getByRole('menuitem', { name: 'Ajouter de la musique', exact: true }).click();
  await page.getByRole('button', { name: 'Retour à la bibliothèque', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Bibliothèque', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Nouvelle playlist', exact: true }).click();
  const playlistName = page.getByRole('textbox', { name: 'Nom de la playlist' });
  await expect(playlistName).toBeFocused();
  await expect(playlistName).toHaveCSS('box-shadow', fieldFocusStroke);
  await page.getByRole('button', { name: 'Retour à la bibliothèque', exact: true }).click();
  await page.getByRole('button', { name: 'Ajouter de la musique', exact: true }).click();
  await page.getByRole('button', { name: 'Fermer le panneau', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Options du lecteur' })).toBeVisible();
});

test('five Glass audio bars follow measured frames and rest on pause', async ({ page }) => {
  await page.addInitScript(() => {
    const listeners = new Set<(event: { data: unknown }) => void>();
    Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
      postMessage: () => {},
      addEventListener: (_: string, callback: (event: { data: unknown }) => void) => listeners.add(callback),
      removeEventListener: (_: string, callback: (event: { data: unknown }) => void) => listeners.delete(callback),
    } } });
    window.addEventListener('audio-fixture', event => listeners.forEach(callback => callback({ data: (event as CustomEvent).detail })));
  });
  await page.goto('/?preview=1');
  await page.getByRole('button', { name: 'Options du lecteur' }).click();
  await page.getByRole('menuitem', { name: /Apparence et écoute/ }).click();
  await page.getByRole('button', { name: 'Compact', exact: true }).click();
  await page.getByRole('button', { name: 'Retour au lecteur', exact: true }).click();
  await page.setViewportSize({ width: 400, height: 260 });
  await expect(page.locator('.audio-glass-bar[data-ogui-glass]')).toHaveCount(5);
  await page.getByRole('button', { name: 'Lancer la lecture', exact: true }).click();
  await expect(page.locator('.audio-visualizer')).toHaveAttribute('data-active', 'true');
  await page.evaluate(() => window.dispatchEvent(new CustomEvent('audio-fixture', { detail: { type: 'visualizer', levels: [.2, .5, .95, .5, .2] } })));
  await expect.poll(() => page.locator('.audio-glass-bar').nth(2).evaluate(element => new DOMMatrix(getComputedStyle(element).transform).d)).toBeCloseTo(.95, 2);
  await page.getByRole('button', { name: 'Mettre en pause', exact: true }).click();
  await expect(page.locator('.audio-visualizer')).toHaveAttribute('data-active', 'false');
  await expect.poll(() => page.locator('.audio-glass-bar').nth(2).evaluate(element => new DOMMatrix(getComputedStyle(element).transform).d)).toBeCloseTo(.13, 2);
});

test('glass aura picker, RGB fade and size controls replace OLED', async ({ page }) => {
  await page.getByRole('button', { name: 'Options du lecteur' }).click();
  await page.getByRole('menuitem', { name: /Apparence et écoute/ }).click();
  await expect(page.getByText(/OLED/)).toHaveCount(0);
  const color = page.getByRole('textbox', { name: 'Couleur hexadécimale' });
  await page.getByRole('slider', { name: 'Teinte de l’aura' }).press('Tab');
  await expect(color).toBeFocused();
  await expect(color).toHaveCSS('box-shadow', fieldFocusStroke);
  await color.fill('#C499F2'); await color.press('Enter');
  await expect(page.locator('.glass-system')).toHaveCSS('--music-aura', 'rgb(196, 153, 242)');
  await page.getByRole('slider', { name: 'Saturation et luminosité' }).focus();
  await page.keyboard.press('ArrowRight');
  await expect(color).not.toHaveValue('#C499F2');
  await page.getByRole('button', { name: 'RGB doux', exact: true }).click();
  await expect(page.locator('html')).toHaveCSS('animation-name', 'aura-cycle');
  const firstColor = await page.locator('.glass-system').evaluate(element => getComputedStyle(element).getPropertyValue('--music-aura'));
  await expect.poll(() => page.locator('.glass-system').evaluate(element => getComputedStyle(element).getPropertyValue('--music-aura'))).not.toBe(firstColor);
  await page.getByRole('button', { name: 'Compact', exact: true }).click();
  const size = page.getByRole('slider', { name: 'Taille du lecteur', exact: true });
  await size.fill('125'); await expect(size).toHaveValue('125');
  await page.getByRole('button', { name: 'Fermer le panneau' }).click();
  await page.getByRole('slider', { name: 'Redimensionner le lecteur', exact: true }).focus();
  await page.keyboard.press('ArrowLeft');
  await expect(page.getByRole('slider', { name: 'Redimensionner le lecteur', exact: true })).toHaveAttribute('aria-valuenow', '120');
});

test('real portaled Glass menu inherits the chosen aura through library color tokens', async ({ page }) => {
  await page.getByRole('button', { name: 'Options du lecteur' }).click();
  await page.getByRole('menuitem', { name: /Apparence et écoute/ }).click();
  const color = page.getByRole('textbox', { name: 'Couleur hexadécimale' });
  await color.fill('#CC3355'); await color.press('Enter');
  await page.getByRole('button', { name: 'Position du lecteur', exact: true }).click();
  const surface = page.locator('.ogui-disclosure__surface');
  await expect(surface).toHaveCSS('--music-aura', 'rgb(204, 51, 85)');
  const first = await surface.evaluate(element => getComputedStyle(element).backgroundColor);
  await page.keyboard.press('Escape');
  await color.fill('#33CC99'); await color.press('Enter');
  await page.getByRole('button', { name: 'Position du lecteur', exact: true }).click();
  await expect(surface).toHaveCSS('--music-aura', 'rgb(51, 204, 153)');
  expect(await surface.evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe(first);
});

test('menu window region contains its final line after native-style viewport expansion', async ({ page }) => {
  await page.setViewportSize({ width: 350, height: 52 });
  await expect(page.locator('.player-mini')).toHaveCSS('height', '52px');
  await page.getByRole('button', { name: 'Options du lecteur' }).click();
  await page.setViewportSize({ width: 350, height: 300 });
  const hide = page.getByRole('menuitem', { name: 'Masquer le widget', exact: true });
  await expect(hide).toBeInViewport({ ratio: 1 });
  await expect.poll(() => page.evaluate(() => {
    const last = window.__musicCommands?.filter(command => command.command === 'shape').at(-1)?.value as { menu?: { x: number; y: number; width: number; height: number } } | undefined;
    const item = [...document.querySelectorAll('[role="menuitem"]')].find(element => element.textContent?.includes('Masquer le widget'))?.getBoundingClientRect();
    if (!last?.menu || !item) return false;
    return last.menu.x <= item.x && last.menu.y <= item.y && last.menu.x + last.menu.width >= item.right && last.menu.y + last.menu.height >= item.bottom;
  })).toBe(true);
  await expect.poll(() => page.evaluate(() => {
    const last = window.__musicCommands?.filter(command => command.command === 'shape').at(-1)?.value as { menu?: { height: number } } | undefined;
    return last?.menu?.height;
  })).toBe(216);
});

test('production menu starts its reveal in the same document before the host answers', async ({ page }) => {
  const material = () => page.locator('.ogui-disclosure__surface').evaluate(element => {
    const style = getComputedStyle(element);
    return { backdrop: style.backdropFilter, background: style.backgroundColor, shadow: style.boxShadow, color: style.color };
  });
  await page.addInitScript(() => {
    const listeners = new Set<(event: { data: unknown }) => void>();
    const commands: unknown[] = [];
    Object.defineProperty(window, '__musicCommands', { value: commands, configurable: true });
    Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
      addEventListener: (_: string, callback: (event: { data: unknown }) => void) => listeners.add(callback),
      removeEventListener: (_: string, callback: (event: { data: unknown }) => void) => listeners.delete(callback),
      postMessage: (message: { command: string }) => {
        commands.push(message);
        if (message.command === 'ready') listeners.forEach(callback => callback({ data: { type: 'state', language: 'fr', pane: 'player', library: [{ id: 0, name: 'Fixture.wav', title: 'Fixture' }] } }));
      },
    } } });
  });
  await page.setViewportSize({ width: 350, height: 288 });
  await page.goto('/');
  const firstFrame = await page.evaluate(() => new Promise<{ running: boolean; hasClip: boolean }>(resolve => {
    const start = performance.now();
    const sample = () => {
      const menu = document.querySelector('.ogui-disclosure__surface');
      if (!menu && performance.now() - start < 1000) { requestAnimationFrame(sample); return; }
      const opening = menu?.getAnimations().find(item => item instanceof CSSAnimation && item.animationName === 'music-menu-appear');
      const frames = (opening?.effect as KeyframeEffect | undefined)?.getKeyframes();
      resolve({ running: opening?.playState === 'running', hasClip: frames?.some(frame => typeof frame.clipPath === 'string') ?? false });
    };
    document.querySelector<HTMLButtonElement>('.options-button')?.click();
    requestAnimationFrame(sample);
  }));
  expect(firstFrame).toEqual({ running: true, hasClip: true });
  await expect(page.getByRole('button', { name: 'Options du lecteur' })).toHaveAttribute('aria-expanded', 'true');
  await expect(page.locator('.player-shell')).toHaveCSS('height', '52px');
  await expect(page.getByRole('menuitem')).toHaveCount(5);
  await expect(page.getByRole('menuitem', { name: 'Masquer le widget', exact: true })).toBeInViewport({ ratio: 1 });
  await expect(page.locator('.ogui-disclosure__surface')).toHaveCSS('border-radius', '26px');
  await expect(page.locator('.ogui-disclosure__surface')).toHaveAttribute('data-ogui-disclosure-surface', '');
  await expect(page.locator('.ogui-disclosure__surface')).toHaveCSS('animation-name', 'music-menu-appear');
  const playerMenu = await material();
  await page.getByRole('menuitem', { name: 'Bibliothèque', exact: true }).click();
  await page.getByRole('button', { name: 'Options de Fixture', exact: true }).click();
  expect(await material()).toEqual(playerMenu);
  await page.keyboard.press('Escape');
  await expect(page.getByRole('menu')).toHaveCount(0);
});

test('device language is used and an unsupported language falls back to English', async ({ page }) => {
  await page.goto('/?preview=1&locale=fr-CA');
  await expect(page.locator('html')).toHaveAttribute('lang', 'fr');
  await page.goto('/?preview=1&locale=de-DE');
  await expect(page.locator('html')).toHaveAttribute('lang', 'en');
  await page.getByRole('button', { name: 'Player options' }).click();
  await page.getByRole('menuitem', { name: 'Appearance and playback' }).click();
  await page.getByRole('button', { name: 'Widget language' }).click();
  await page.getByRole('menuitem', { name: 'Français', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Paramètres' })).toBeVisible();
  await expect(page.locator('html')).toHaveAttribute('lang', 'fr');
  await page.getByRole('button', { name: 'Langue du widget' }).click();
  await page.getByRole('menuitem', { name: 'Langue de Windows', exact: true }).click();
  await expect(page.locator('html')).toHaveAttribute('lang', 'en');
  await page.getByRole('button', { name: 'Close panel' }).click();
  await page.getByRole('button', { name: 'Player options' }).click();
  await page.getByRole('menuitem', { name: 'Add music' }).click();
  await expect(page.getByRole('tab', { name: 'Search', exact: true })).toHaveAttribute('aria-selected', 'true');
  await page.getByRole('tab', { name: 'Link or file' }).click();
  await expect(page.getByRole('textbox', { name: 'YouTube or Spotify link' })).toBeVisible();
});

test('six shortcuts can be recorded or disabled and duplicate bindings are preserved', async ({ page }) => {
  await page.getByRole('button', { name: 'Options du lecteur' }).click();
  await page.getByRole('menuitem', { name: 'Apparence et écoute' }).click();
  const group = page.getByRole('region', { name: 'Raccourcis globaux' });
  await expect(group.locator('.shortcut-record')).toHaveCount(6);
  const next = group.getByRole('button', { name: 'Modifier le raccourci · Morceau suivant' });
  await next.click();
  await expect(next).toHaveText('Appuyez sur les touches…');
  await next.press('F3');
  await expect(group.getByRole('alert')).toContainText('Utilisez Ctrl ou Alt');
  await next.press('Control+Shift+M');
  await expect(group.getByRole('alert')).toContainText('déjà utilisé');
  await expect(next).toHaveText('Ctrl + Alt + Maj + →');
  await next.click();
  await next.press('Control+Alt+F9');
  await expect(next).toHaveText('Ctrl + Alt + F9');
  await expect(group.getByRole('alert')).toHaveCount(0);
  await group.getByRole('button', { name: 'Désactiver le raccourci · Morceau suivant' }).click();
  await expect(next).toHaveText('Aucun');
});

test('ambient glass and the marquee pause in the background and resume under interaction', async ({ page }) => {
  await page.addInitScript(() => {
    const listeners = new Set<(event: { data: unknown }) => void>();
    Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
      addEventListener: (_: string, listener: (event: { data: unknown }) => void) => listeners.add(listener),
      removeEventListener: (_: string, listener: (event: { data: unknown }) => void) => listeners.delete(listener),
      postMessage: (message: { command: string }) => {
        if (message.command === 'ready') listeners.forEach(listener => listener({ data: {
          type: 'state', title: 'A long track title to exercise the background marquee in the music player',
          design: 'Minimalist', interactive: false, auraMode: 'RGB',
        } }));
      },
    } } });
    window.addEventListener('widget-activity', event => listeners.forEach(listener =>
      listener({ data: { type: 'state', interactive: (event as CustomEvent<boolean>).detail } })));
  });
  await page.setViewportSize({ width: 350, height: 52 });
  await page.goto('/');
  const aura = page.locator('html.aura-rgb');
  const title = page.locator('.mini-title[data-scroll-title] > span');
  await expect(aura).toHaveCSS('animation-play-state', 'paused');
  await expect(title).toHaveCSS('animation-play-state', 'paused');
  await page.evaluate(() => window.dispatchEvent(new CustomEvent('widget-activity', { detail: true })));
  await expect(aura).toHaveCSS('animation-play-state', 'running');
  await expect(title).toHaveCSS('animation-play-state', 'running');
  await page.evaluate(() => window.dispatchEvent(new CustomEvent('widget-activity', { detail: false })));
  await expect(aura).toHaveCSS('animation-play-state', 'paused');
  await expect(title).toHaveCSS('animation-play-state', 'paused');
});

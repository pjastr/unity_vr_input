# VR Input Demo – Meta Quest 3S (Unity 6.3 LTS)

Projekt demonstracyjny pokazujący **różne sposoby wprowadzania danych w VR** na goglach Meta Quest 3S:

- **kontrolery Touch Plus** – przyciski, triggery, gripy, gałki, dotyk pojemnościowy, wibracje (`OVRInput`),
- **śledzenie dłoni** – szczypnięcie (pinch), gesty statyczne (pięść, V, kciuk w górę/dół, wskazywanie, otwarta dłoń), mikrogesty kciuka (`OVRHand`, `OVRSkeleton`),
- **akcje wywoływane tymi wejściami** – chwytanie i rzucanie, promień wskazujący, wciskanie przycisków palcem (poke), strzelanie, chodzenie i obrót, zmiana kolorów i grawitacji, reset sceny,
- **osobna strefa Interaction SDK (ISDK)** – te same rodzaje interakcji zrobione gotowymi blokami Meta.

Scena składa się wyłącznie z **prymitywów Unity** (kostki, kule, walce, kapsuły, płaszczyzny), **paneli UI** oraz **Building Blocks z Meta XR SDK** (rig kamery, dłonie, kontrolery, interakcje ISDK). Nie ma żadnych zewnętrznych modeli ani tekstur.

---

## Spis treści

1. [Wymagania](#1-wymagania)
2. [Zawartość projektu](#2-zawartość-projektu)
3. [Pierwsze otwarcie i konfiguracja](#3-pierwsze-otwarcie-i-konfiguracja)
4. [Build & Run przez USB](#4-build--run-przez-usb)
5. [Debugowanie przez USB](#5-debugowanie-przez-usb)
6. [Instrukcja obsługi w goglach](#6-instrukcja-obsługi-w-goglach)
7. [Jak to działa](#7-jak-to-działa)
8. [Strefa Interaction SDK](#8-strefa-interaction-sdk)
9. [Dostrajanie i rozbudowa](#9-dostrajanie-i-rozbudowa)
10. [Rozwiązywanie problemów](#10-rozwiązywanie-problemów)

---

## 1. Wymagania

| Element | Wersja / uwagi |
|---|---|
| Unity | **6000.3.24f1** (Unity 6.3 LTS) z modułem **Android Build Support** (w tym *OpenJDK* oraz *Android SDK & NDK Tools*) – instalacja przez Unity Hub › Installs › Add modules |
| Gogle | Meta Quest 3S z aktualnym Horizon OS, **tryb deweloperski włączony** |
| Kabel | USB-C z transmisją danych (nie tylko do ładowania) |
| Konto | Konto Meta z utworzoną organizacją deweloperską (potrzebne do włączenia trybu deweloperskiego) |
| Windows | Sterownik ADB dla Quest – najprościej zainstalować **Meta Quest Developer Hub (MQDH)**; bez niego Unity nie widzi gogli |

Pakiety są wpisane w `Packages/manifest.json` i instalują się same przy pierwszym otwarciu projektu:

| Pakiet | Wersja | Rola |
|---|---|---|
| `com.meta.xr.sdk.core` | 207.0.1 | Meta XR Core SDK: `OVRCameraRig`, `OVRManager`, `OVRInput`, `OVRHand`, `OVRSkeleton` |
| `com.meta.xr.sdk.interaction.ovr` | 207.0.1 | Interaction SDK (+ integracja z OVR) – strefa ISDK |
| `com.unity.xr.openxr` | 1.18.0 | Backend OpenXR (zalecany przez Meta od SDK v74) |
| `com.unity.render-pipelines.universal` | 17.3.0 | URP |
| `com.unity.inputsystem` | 1.18.0 | Wymagany przez OpenXR |
| `com.unity.ugui` | 2.0.0 | Panele tekstowe (Canvas + Text) |

> Jeśli Package Manager zgłosi, że którejś wersji nie ma, otwórz **Window › Package Manager**, znajdź pakiet i wybierz wersję proponowaną dla Twojego edytora. Kod demo korzysta tylko z podstawowego API, które jest stabilne między wersjami SDK.

---

## 2. Zawartość projektu

```
unity_vr_input/
├─ INSTRUKCJA.md                  ← ten plik
├─ Packages/manifest.json         ← lista pakietów
├─ ProjectSettings/ProjectVersion.txt
└─ Assets/VRInputDemo/
   ├─ Editor/
   │  └─ VRInputDemoSetup.cs      ← menu "VR Input Demo": konfiguracja, budowa sceny, sprawdzanie i układanie bloków, Build & Run
   ├─ Scripts/
   │  ├─ XRInputSource.cs         ← wspólne źródło wejścia: kontroler albo dłoń (rdzeń projektu)
   │  ├─ HandGestureDetector.cs   ← rozpoznawanie gestów i mikrogestów
   │  ├─ DemoActions.cs           ← mapowanie przycisków/gestów na akcje
   │  ├─ SimpleGrabber.cs         ← chwytanie i rzucanie
   │  ├─ Grabbable.cs             ← obiekt do chwytania
   │  ├─ RayPointer.cs            ← promień wskazujący
   │  ├─ RayTarget.cs             ← cel dla promienia i pocisków
   │  ├─ PokeButton.cs            ← przycisk wciskany palcem
   │  ├─ PlayerLocomotion.cs      ← chodzenie i obrót
   │  ├─ ControllerInputPanel.cs  ← panel na żywo: stan OVRInput
   │  ├─ HandInfoPanel.cs         ← panel na żywo: stan OVRHand i gesty
   │  ├─ DemoLog.cs / DemoLogPanel.cs ← dziennik zdarzeń (panel + logcat)
   │  ├─ DemoUI.cs, ColorTint.cs, DemoResources.cs ← pomocnicze (prymitywy, panele, kolory)
   │  ├─ Projectile.cs
   │  └─ IsdkZone.cs              ← znacznik strefy Interaction SDK
   ├─ Scenes/InputDemo.unity      ← tworzona przez menu VR Input Demo › 2
   ├─ Materials/DemoLit.mat       ← tworzony automatycznie (URP/Lit)
   └─ Settings/VRInputDemo_URP*.asset ← tworzone automatycznie (URP)
```

Podział pracy:
- **skrypt edytora** buduje otoczenie, obiekty, panele, logikę demo i stoliki strefy ISDK,
- **Building Blocks** dostarczają wszystko, co pochodzi z Meta: rig kamery, dłonie, kontrolery i interakcje ISDK.

Skrypty demo same znajdują w scenie `OVRCameraRig` i `OVRHand` z bloków – nic nie trzeba podpinać ręcznie.

---

## 3. Pierwsze otwarcie i konfiguracja

### Krok 0 – otwarcie projektu
1. Unity Hub › **Add › Add project from disk** › wskaż folder `unity_vr_input`.
2. Otwórz projekt w **6000.3.24f1**. Pierwsze otwarcie trwa kilka minut (pobieranie i import pakietów).
3. Mogą pojawić się okna Meta SDK (np. zgoda na telemetrię, propozycja restartu z nowym systemem wejścia) – zaakceptuj domyślne odpowiedzi. Restart edytora jest normalny.
4. Po imporcie pojawi się okno **VR Input Demo** z propozycją konfiguracji – kliknij **Konfiguruj teraz** (albo zrób to z menu, krok 1).

### Krok 1 – `VR Input Demo › 1. Konfiguruj projekt dla Meta Quest`
Skrypt ustawia:
- Android: **IL2CPP**, **ARM64**, **min. API 32**, **Vulkan**, orientacja pozioma, identyfikator `com.vrinputdemo.quest`,
- **Linear** color space,
- **URP**: tworzy `VRInputDemo_URP.asset` (MSAA 4×, bez HDR i bez tekstur głębi/koloru – ustawienia przyjazne dla Quest) i przypina go w Graphics/Quality,
- **Hand Tracking Support = Controllers And Hands**,
- **Development Build + Script Debugging** (do debugowania przez USB),
- na koniec proponuje **przełączenie platformy na Android** – zgódź się (trwa kilka minut).

### Krok 2 – XR Plug-in Management (ręcznie, jednorazowo)
1. **Edit › Project Settings › XR Plug-in Management**.
2. Zakładka **Android** (ikona robota): zaznacz **OpenXR**, a poniżej grupę funkcji **Meta XR**.
3. (Opcjonalnie) zakładka **Windows/Standalone**: to samo – pozwala testować przez Quest Link w trybie Play.

### Krok 3 – Project Setup Tool
**Meta › Tools › Project Setup Tool**. Na zakładce **Android** kliknij **Fix All**, potem **Apply All**. Powtarzaj, aż nie zostaną błędy wymagane („Required”).

### Krok 4 – `VR Input Demo › 2. Zbuduj scenę demo`
Tworzy `Assets/VRInputDemo/Scenes/InputDemo.unity` (otoczenie, stół, obiekty, przyciski, cele, panele, logika demo, stoliki strefy ISDK) i dodaje ją do listy scen builda. Scena nie ma jeszcze kamery VR – tę dają bloki w następnym kroku.

### Krok 5 – Building Blocks
Otwórz **Meta › Tools › Building Blocks** (w SDK 207 także *Window › Meta › Meta XR SDK › Building Blocks*). Dla każdego bloku z tabeli: **kliknij w puste miejsce w Hierarchy** (nic nie może być zaznaczone), znajdź kafelek i kliknij **Add Block**. Zależności dodają się same.

| Blok | Po co | Wymagany |
|---|---|---|
| **Camera Rig** | kamera VR, `OVRManager`, punkty zaczepienia dłoni i kontrolerów | tak |
| **Hand Tracking** | dane i model dłoni (`OVRHand`, `OVRSkeleton`) – potrzebne do gestów | tak |
| **Controller Tracking** | modele kontrolerów | zalecany |
| **Passthrough** | obraz z kamer gogli zamiast tła | nie |

Bloki ISDK (strefa za plecami) opisuje [punkt 8](#8-strefa-interaction-sdk) – możesz je dodać od razu albo później.

### Krok 6 – `VR Input Demo › 3. Sprawdź bloki w scenie`
Pokazuje raport `[OK]` / `[BRAK]` dla każdego bloku, ostrzega przed zdublowanym rigiem lub zbędną kamerą i od razu ustawia w `OVRManager` **Tracking Origin = Floor Level** (wysokość sceny = podłoga pokoju). Gdy wszystko jest `[OK]`, zapisz scenę (**Ctrl+S**).

> Ponowne uruchomienie kroku 4 (np. po zmianach w skryptach) **odświeża** tylko obiekty demo – Building Blocks i strefa ISDK zostają w scenie.

---

## 4. Build & Run przez USB

### Przygotowanie gogli (jednorazowo)
1. Aplikacja **Meta Horizon** na telefonie › Urządzenia › Quest 3S › **Ustawienia gogli › Tryb dewelopera** › włącz.
2. W goglach: **Ustawienia › Ruch i śledzenie › Śledzenie dłoni** – włącz, razem z **automatycznym przełączaniem między dłońmi a kontrolerami**.
3. Podłącz gogle kablem USB-C do komputera. W goglach pojawi się pytanie **„Zezwolić na debugowanie USB?”** – zaznacz *Zawsze zezwalaj z tego komputera* i potwierdź.
4. Sprawdź połączenie w Unity: **File › Build Profiles › Android** › lista **Run Device** › **Refresh** – gogle powinny pojawić się na liście.

### Build & Run
**Sposób A (najprostszy):** `VR Input Demo › 4. Build And Run (USB)`
– sprawdza bloki w scenie, buduje `Builds/VRInputDemo.apk` (Development + Script Debugging), instaluje na goglach i uruchamia.

**Sposób B (standardowy):** **File › Build Profiles**
1. Wybierz platformę **Android** (lub profil **Meta Quest**, jeśli używasz Build Profiles) – musi być aktywna.
2. **Run Device**: wybierz Quest 3S (przycisk *Refresh*, jeśli go nie widać).
3. Zaznacz **Development Build** i **Script Debugging** (opcjonalnie *Autoconnect Profiler*).
4. **Build And Run** › podaj nazwę pliku APK (np. w folderze `Builds/`).

Aplikacja uruchomi się w goglach automatycznie. Później znajdziesz ją w **Biblioteka › Nieznane źródła** (*Unknown Sources*).

---

## 5. Debugowanie przez USB

| Co | Jak |
|---|---|
| **Logi w edytorze** | Okno **Console** › lista *Editor* (lewy górny róg) › wybierz gogle (*AndroidPlayer…*). Działa dla Development Build. |
| **Logi systemowe gogli** | Pakiet **Android Logcat** (Package Manager › Unity Registry › *Android Logcat*) › *Window › Analysis › Android Logcat*. Filtr: `VRInputDemo` – każde zdarzenie demo ma ten prefiks. |
| **Debugger C# (breakpointy)** | Build z *Script Debugging*. Visual Studio: **Debug › Attach Unity Debugger** › wybierz gogle (USB). Rider: **Run › Attach to Unity Process**. Aby złapać kod startowy, zaznacz w buildzie **Wait For Managed Debugger**. |
| **Profiler** | **Window › Analysis › Profiler** › lista *Play Mode/Editor* › wybierz gogle. |

Każda akcja w demo trafia jednocześnie na panel **Dziennik zdarzeń** w VR i do logów w edytorze (Console / Android Logcat) – łatwo porównać to, co się dzieje w goglach, z tym, co widzi komputer.

---

## 6. Instrukcja obsługi w goglach

### Układ sceny (widok z góry, patrzysz w stronę „przodu”)

```
                        [Cel górny]
   [Cel lewy]                                [Cel prawy]
                 ┌──────── POMOC ────────┐
                 │ (mapowanie wejść)     │
                 ├──── DZIENNIK ZDARZEŃ ─┤
   [Panel                                       [Panel
   KONTROLERY]   ┌──────── STÓŁ ────────┐       DŁONIE]
                 │ obiekty │ 4 przyciski│
                 └──────────────────────┘
                         ( TY )   ← znacznik startu
                 ┌──── STÓŁ ISDK ───────┐
                 │ stanowiska 1–4       │   [Tablica ISDK]
                 └──────────────────────┘
                   [5] cel promienia ISDK
                   [6] podłoga teleportu ISDK
```

Po starcie stoisz na turkusowym znaczniku. Przed Tobą stół, z lewej panel kontrolerów, z prawej panel dłoni, dalej pomoc i dziennik. **Strefa Interaction SDK jest za Twoimi plecami.**

### Kontrolery Touch Plus

| Wejście | Akcja |
|---|---|
| **Trigger** (palec wskazujący) | Wybór promieniem: cel zmienia kolor, przycisk się wciska, obiekt ze stołu przylatuje do ręki |
| **Grip** (środkowy palec) | Chwyć obiekt w zasięgu ręki; puść w ruchu = **rzut** |
| **A** | Strzał pociskiem (prawy kontroler) |
| **B** | Losowe kolory obiektów |
| **X** | Nowa kula przy lewej ręce |
| **Y** | Test wibracji: narastająca (L), wysoka częstotliwość (P), trzy impulsy (oba) |
| **Menu (☰)** | Reset sceny |
| **Lewa gałka** | Chodzenie w kierunku patrzenia |
| **Prawa gałka ←/→** | Obrót o 45° |
| **Klik lewej gałki** | Pokaż/ukryj panele |
| **Klik prawej gałki** | Powrót na start |
| **Końcówka kontrolera** | Wciskanie przycisków na stole (poke) |

> Gdy w scenie jest blok **Interactions Rig** (strefa ISDK, punkt 8), gałkami steruje ISDK (teleport i obrót), a chodzenie i obrót gałkami z demo są wyłączone. Pozostałe przyciski działają bez zmian.

Na panelu **KONTROLERY** widać na żywo wszystkie odczyty: wartości triggera i gripu (0–1), położenie gałki, stan przycisków (*dotyk* / *WCIŚNIĘTY*), dotyk pojemnościowy, zbliżenie palca, pozycję, prędkość i stan śledzenia. Pod tekstem są wskaźniki 3D: słupki trigger/grip, gałka z kropką i kule przycisków (szary = puszczony, żółty = dotyk, zielony = wciśnięty).

### Dłonie (odłóż kontrolery)

| Gest | Akcja |
|---|---|
| **Pinch** (kciuk + palec wskazujący) | Chwyt z bliska **albo** wybór promieniem z dłoni (jak trigger) |
| **Pięść** | Chwyt obiektu (trzymany w środku dłoni) |
| **Wyprostowany palec wskazujący** | Wciskanie przycisków na stole (czubek palca = turkusowa kulka) |
| **Znak V** (wskazujący + środkowy), 0,3 s | Nowa kula nad dłonią |
| **Kciuk w górę**, przytrzymaj 1 s | Reset sceny |
| **Kciuk w dół**, przytrzymaj 1 s | Pokaż/ukryj panele |
| **Mikrogest:** przesunięcie kciuka w lewo/prawo po boku palca wskazującego | Obrót o 45° |
| **Mikrogest:** przesunięcie do przodu/do tyłu | Krok 0,75 m do przodu/do tyłu |
| **Mikrogest:** stuknięcie kciukiem | Strzał pociskiem z dłoni |

Na panelu **DŁONIE** widać: czy dłoń jest śledzona i z jaką pewnością, rozpoznany gest i czas jego trwania, siłę szczypnięcia każdego palca, **współczynniki wyprostu palców** (K/W/Ś/S/M – przydatne do strojenia progów), ostatni mikrogest, ważność pozy promienia i czy trwa gest systemowy.

> Uwaga: gest „otwarta lewa dłoń zwrócona do siebie + pinch” to **gest systemowy** (menu Quest) – w tym czasie demo ignoruje wejście z tej dłoni.

### Stół

- **Lewa część – obiekty do chwytania:** kostki, kule, kapsuła i walec. Chwytaj, przekładaj z ręki do ręki, rzucaj, przyciągaj promieniem. Obiekt pod ręką lub promieniem jaśnieje.
- **Prawa część – 4 przyciski poke:** *Nowa kula*, *Kolory*, *Grawitacja* (obiekty zaczynają unosić się w powietrzu), *Reset*. Wciska się je od góry palcem lub końcówką kontrolera albo zdalnie promieniem.
- **Cele** (dwie duże kule po bokach, obracająca się kostka wysoko): wybierz promieniem albo traf pociskiem – zmienią kolor.

---

## 7. Jak to działa

### 7.1 Architektura

```
OVRManager ──► OVRInput (kontrolery)          OVRHand + OVRSkeleton (dłonie)
                     │                                     │
                     │                          HandGestureDetector (gesty, mikrogesty)
                     ▼                                     ▼
              ┌─────────── XRInputSource (Left / Right) ───────────┐
              │  IsControllerActive / IsHandTracked                 │
              │  Select (trigger | pinch)   Grab (grip | pinch|pięść)│
              │  GrabPoint, Pointer (promień), PokePoint, Velocity  │
              │  Haptic()                                           │
              └───────┬──────────────┬─────────────┬───────────────┘
                      ▼              ▼             ▼
               SimpleGrabber     RayPointer    PokeButton        DemoActions
               (chwyt, rzut)     (promień)     (przyciski)       (A/B/X/Y/Menu, gesty,
                                                                 mikrogesty → akcje)
```

Kluczowa idea: **`XRInputSource` tłumaczy kontroler i dłoń na te same, abstrakcyjne akcje** („wybierz”, „chwyć”, „punkt chwytu”, „promień”, „punkt poke”). Dzięki temu chwytanie, promień i przyciski mają jedną implementację, która działa zarówno z kontrolerami, jak i z dłońmi – a przełączanie następuje automatycznie, gdy odłożysz kontrolery.

### 7.2 Kontrolery – `OVRInput`
`OVRManager` co klatkę wywołuje `OVRInput.Update()`, a skrypty odczytują stan:

```csharp
// wartości analogowe 0..1
float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
float grip    = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger,  OVRInput.Controller.RTouch);
Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick,   OVRInput.Controller.LTouch);

// przyciski: Get = trzymany, GetDown = wciśnięty w tej klatce, GetUp = puszczony
bool a = OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch);   // A
bool x = OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch);   // X

// dotyk pojemnościowy i zbliżenie palca
bool touchA  = OVRInput.Get(OVRInput.Touch.One, OVRInput.Controller.RTouch);
bool nearTrg = OVRInput.Get(OVRInput.NearTouch.PrimaryIndexTrigger, OVRInput.Controller.RTouch);

// wibracje: częstotliwość 0..1, amplituda 0..1 (wyłączenie: 0, 0)
OVRInput.SetControllerVibration(0.6f, 0.5f, OVRInput.Controller.RTouch);
```

`Button.One/Two` oznaczają **A/B** z maską `RTouch` i **X/Y** z maską `LTouch` („Primary” zawsze odnosi się do wskazanego kontrolera). Trigger i grip są zamieniane na zdarzenia z **histerezą** (wciśnięcie > 0,55, puszczenie < 0,35), żeby uniknąć „drgania” na granicy progu.

### 7.3 Dłonie – `OVRHand`
```csharp
bool tracked   = hand.IsTracked;
bool pinching  = hand.GetFingerIsPinching(OVRHand.HandFinger.Index);   // pinch kciuk+wskazujący
float strength = hand.GetFingerPinchStrength(OVRHand.HandFinger.Index); // 0..1
Transform ray  = hand.PointerPose;                                      // systemowy promień dłoni
var micro      = hand.GetMicrogestureType();                            // SwipeLeft, ThumbTap, ...
```
Promień z dłoni korzysta z **`PointerPose`** – systemowej pozy wyliczanej z ramienia i dłoni (stabilniejszej niż kierunek samego palca).

### 7.4 Rozpoznawanie gestów – `HandGestureDetector`
Dla każdego palca liczony jest **współczynnik wyprostu**:

```
ratio = |czubek palca − nadgarstek| / |kostka palca (MCP) − nadgarstek|
```

Palec wyprostowany ma ok. **1,8–2,0**, zgięty w pięść ok. **0,8–1,1**. Progi: wyprostowany > 1,5, zgięty < 1,25. Kciuk: odległość czubka kciuka od kostki palca wskazującego, podzielona przez długość dłoni (> 0,7 = odstaje). Kierunek kciuka (składowa Y) odróżnia „kciuk w górę” od „kciuk w dół”.

| Gest | Warunek |
|---|---|
| Pinch | `GetFingerIsPinching(Index)` (ma pierwszeństwo) |
| Pięść | 4 palce zgięte, kciuk przy dłoni |
| Kciuk w górę / w dół | 4 palce zgięte, kciuk odstaje i wskazuje w górę / w dół |
| Otwarta dłoń | 4 palce wyprostowane |
| Wskazywanie | wskazujący wyprostowany, pozostałe zgięte |
| Znak V | wskazujący i środkowy wyprostowane, serdeczny i mały zgięte |

Gest musi być stabilny przez 0,12 s (oprócz pinch), żeby nie migotał przy przejściach. Kości są wyszukiwane **po nazwach**, więc detektor działa z oboma formatami szkieletu Meta (starszym `Hand_IndexTip` i OpenXR `XRHand_IndexTip`).

**Mikrogesty** to małe ruchy kciuka po boku palca wskazującego (przesunięcia w 4 kierunkach i stuknięcie), rozpoznawane przez system. Demo używa ich do poruszania się bez kontrolerów.

### 7.5 Akcje
- **Chwytanie (`SimpleGrabber`)** – w promieniu 7 cm (kontroler) / 9 cm (dłoń) od punktu chwytu szuka najbliższego `Grabbable`. Po chwycie obiekt staje się kinematyczny i podąża za ręką z zachowaniem przesunięcia. Przy puszczeniu dostaje **prędkość ręki** – z `OVRInput.GetLocalControllerVelocity` dla kontrolera, a dla dłoni liczoną z historii pozycji z ostatnich klatek.
- **Promień (`RayPointer`)** – `Physics.Raycast` z pozy wskaźnika. Kolor promienia: biały (nic), turkusowy (coś interaktywnego), żółty (trwa wybór). Przyciąganie obiektu to rzut balistyczny: `v = d/t − ½·g·t`.
- **Poke (`PokeButton`)** – bez fizyki: co klatkę pozycja czubka palca/kontrolera jest przeliczana do układu przycisku. Wciśnięcie liczy się tylko przy wejściu **od góry** i przekroczeniu 70% skoku – boczne muśnięcie nie wciska.
- **Haptyka** – krótkie impulsy przy najechaniu, chwycie, wciśnięciu i strzale (tylko kontrolery).
- **Lokomocja (`PlayerLocomotion`)** – przesuwa cały `OVRCameraRig`; obrót odbywa się wokół głowy, więc nie „wyrzuca” gracza w bok.

---

## 8. Strefa Interaction SDK

Za plecami gracza stoi stół ISDK z **6 ponumerowanymi stanowiskami**. Na każde trafia gotowy obiekt utworzony przez jeden Building Block ISDK. Bloki tworzą kompletne, podpięte obiekty same – nie nakłada się ich na prymitywy demo.

Widok po odwróceniu się (stoisz na dole rysunku):

```
                       [6] Teleport (podłoga)
                       [5] Ray (cel w powietrzu)
              ┌──────────────── STÓŁ ISDK ────────────────┐
  [tablica]   │ [1] Grab   [2] Touch       [3] Distance   │
              │                            [4] Poke       │
              └───────────────────────────────────────────┘
                              ( TY )
```

### Krok 1 – rig interakcji
1. Kliknij w puste miejsce w Hierarchy, żeby **nic nie było zaznaczone**.
2. **Building Blocks › Interactions Rig › Add Block.**
3. `[BuildingBlock] OVRComprehensiveInteractionRig` ma leżeć na **najwyższym poziomie** Hierarchy, obok `[BuildingBlock] Camera Rig`. Jeśli trafił gdzie indziej, przeciągnij go wyżej.

### Krok 2 – bloki interakcji
Dla każdego bloku z tabeli: **odznacz wszystko w Hierarchy** i kliknij **Add Block**. Nie upuszczaj bloków na obiekty sceny – blok sam utworzy swój obiekt.

| Nr | Blok | Co sprawdzić w goglach |
|---|---|---|
| 1 | **Grab Interaction** | chwyć (pinch lub grip), przełóż do drugiej ręki, rzuć |
| 2 | **Touch Hand Grab** | chwyć dłonią – palce układają się na obiekcie |
| 3 | **Distance Grab** | wskaż obiekt z daleka i chwyć – przyleci do ręki |
| 4 | **Poke Interaction** | wciśnij przycisk palcem lub końcówką kontrolera |
| 5 | **Ray Interaction** | wskaż cel promieniem i wybierz (trigger / pinch) – cel zmienia kolor |
| 6 | **Teleport** | gałka do przodu, wskaż podłogę, puść |

### Krok 3 – ułożenie na stanowiskach
**VR Input Demo › Ułóż bloki ISDK na stanowiskach.** Polecenie samo rozpozna obiekty z bloków, przeniesie je na właściwe stanowiska i pokaże, których bloków jeszcze brakuje. Potem **Ctrl+S**.

Jeśli przycisk (4) albo cel (5) jest zwrócony do Ciebie tyłem, zaznacz go i obróć o 180° w osi Y.

### Krok 4 – sprawdzenie
**VR Input Demo › 3. Sprawdź bloki w scenie** – przy *Interactions Rig* zobaczysz też licznik bloków ISDK (np. `6/6`).

### Jak demo zachowuje się przy rigu ISDK
Przy starcie demo wykrywa blok Interactions Rig i:
- **ukrywa modele dłoni i kontrolerów** z bloków Hand Tracking / Controller Tracking – ISDK rysuje własne (gesty demo działają dalej),
- **wyłącza chodzenie i obrót gałkami** z demo – gałki przejmuje ISDK (teleport, obrót); mikrogesty działają dalej,
- **chowa własny promień** nad strefą ISDK; skrypty demo nie chwytają obiektów ISDK.

Oba zachowania można wyłączyć w obiekcie **Demo › DemoActions**: *Hide Block Visuals When Isdk Rig Present* i *Disable Own Locomotion When Isdk Rig Present*.

---

## 9. Dostrajanie i rozbudowa

**Progi gestów** – obiekt `InputSources/LeftInput` (i `RightInput`) › komponent **Hand Gesture Detector**. Odczytaj współczynniki z panelu „Dłonie” dla swojej dłoni i dopasuj *Finger Extended Ratio*, *Finger Curled Ratio*, *Thumb Extended Ratio*.

**Progi triggera/gripu** – komponent **XR Input Source**: *Press Threshold* / *Release Threshold*.

**Nowa akcja na przycisk** – w `DemoActions.Update()`:
```csharp
if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch))
    DemoLog.Add("Moja akcja!");
```

**Nowa akcja na gest** – w `DemoActions.HandleHand()`:
```csharp
else if (g.Current == HandGesture.OpenPalm && held > 0.5f)
{
    RandomizeColors($"{src.Label}: otwarta dłoń");
    fired = true;
}
```

**Nowy gest** – dodaj wartość do `HandGesture` i warunek w `HandGestureDetector.Classify()` (masz do dyspozycji `Ratios[0..4]`, `ThumbDirection`, `PalmCenter`, `IndexTip`).

**Odświeżenie sceny** – po zmianach w `VRInputDemoSetup.cs` uruchom ponownie *VR Input Demo › 2. Zbuduj scenę demo*. Obiekty demo powstaną od nowa, a Building Blocks i strefa ISDK zostaną. Aby odbudować także strefę ISDK, usuń wcześniej obiekt `ISDK_Zone` (bloki ISDK trzeba wtedy dodać ponownie).

---

## 10. Rozwiązywanie problemów

Zacznij od **VR Input Demo › 3. Sprawdź bloki w scenie** – wskazuje większość problemów ze sceną.

| Problem | Rozwiązanie |
|---|---|
| Gogle nie są widoczne w *Run Device* | Kabel z transmisją danych, potwierdź „Zezwól na debugowanie USB” w goglach, zainstaluj MQDH (sterownik ADB), sprawdź tryb dewelopera, kliknij *Refresh*. |
| Gogle są na liście, ale instalacja się nie udaje | W goglach nie potwierdzono debugowania USB – odłącz i podłącz kabel, potwierdź okno w goglach. |
| Aplikacja startuje jako płaskie okno 2D | Nie zaznaczono **OpenXR + Meta XR** w *XR Plug-in Management › Android* (krok 2) albo brak bloku **Camera Rig**. |
| Obraz zdublowany / dwie kamery | W scenie są dwa rigi lub zostawiona *Main Camera* – zostaw jeden blok Camera Rig. |
| Stół na wysokości głowy / pod podłogą | Uruchom *3. Sprawdź bloki* (ustawia Floor Level) i zresetuj granicę (Guardian) w goglach. |
| Czarny ekran / błędy URP | Uruchom ponownie krok 1 (przypina URP) i *Project Setup Tool › Fix All*. |
| Panel „Dłonie” pisze „Brak OVRHand” | Dodaj blok **Hand Tracking**. |
| Dłonie nie są śledzone | Włącz śledzenie dłoni w goglach i odłóż kontrolery; sprawdź *OVRManager › Quest Features › Hand Tracking Support = Controllers And Hands*. |
| Gesty rozpoznają się źle | Dostrój progi (punkt 9) na podstawie wartości z panelu „Dłonie”. |
| Mikrogesty nie działają | Wymagają aktualnego Horizon OS i śledzenia dłoni; sprawdź zalecenia w *Project Setup Tool*. |
| Podwójne dłonie po dodaniu ISDK | W *Demo › DemoActions* zaznacz *Hide Block Visuals When Isdk Rig Present*. |
| Gałki nie poruszają graczem | Jest blok Interactions Rig – lokomocję przejmuje ISDK (punkt 8); możesz to zmienić w *DemoActions*. |
| Brak logów w Console | Build musi być *Development*; w Console wybierz gogle z listy połączeń albo użyj okna *Android Logcat*. |

---

### Przydatne linki
- [Meta – Building Blocks](https://developers.meta.com/horizon/documentation/unity/bb-overview/)
- [Meta – Unity project setup](https://developers.meta.com/horizon/documentation/unity/unity-project-setup/)
- [Meta – ręczna konfiguracja (OpenXR, Project Setup Tool)](https://developers.meta.com/horizon/documentation/unity/unity-tutorial-hello-world-manual-steps/)
- [Meta – API OVRHand](https://developers.meta.com/horizon/reference/unity/v207/class_o_v_r_hand) · [OVRSkeleton](https://developers.meta.com/horizon/reference/unity/v207/class_o_v_r_skeleton) · [OVRCameraRig](https://developers.meta.com/horizon/reference/unity/v207/class_o_v_r_camera_rig)

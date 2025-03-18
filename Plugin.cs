using BepInEx;
using HarmonyLib;
using System.Reflection;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BepInEx.Configuration;
using BoplFixedMath;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using Steamworks.ServerList;
using System.Linq;

namespace AudioController
{
    [BepInPlugin("com.maxgamertyper1.audiocontroller", "Audio Controller", "1.0.1")]
    public class AudioController : BaseUnityPlugin
    {
        public static string CustomSongsPath = Path.Combine(Paths.ConfigPath, "CustomSongs");

        internal static ConfigFile config;
        internal static ConfigEntry<bool> OverrideOtherSongs;
        internal static ConfigEntry<bool> LoadCustomSongs;
        private void Log(string message)
        {
            Logger.LogInfo(message);
        }

        private void Awake()
        {
            // Plugin startup logic
            Log($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            DoPatching();
            CharacterSelectHandler_online.clientSideMods_you_can_increment_this_to_enable_matchmaking_for_your_mods__please_dont_use_it_to_cheat_thats_really_cringe_especially_if_its_desyncing_others___you_didnt_even_win_on_your_opponents_screen___I_cannot_imagine_a_sadder_existence += 1;

            if (!Directory.Exists(CustomSongsPath))
            {
                Directory.CreateDirectory(CustomSongsPath);
                Logger.LogInfo($"Created custom folder at: {CustomSongsPath}");
            }
            config = ((BaseUnityPlugin)this).Config;
            OverrideOtherSongs = config.Bind<bool>("Songs", "Override Vanilla Songs", false, "Determines if Custom Songs will override the vanilla songs, removes the vanilla songs");
            LoadCustomSongs = config.Bind<bool>("Songs", "Load Custom Songs", true, "Determines if Custom Songs will be loaded");
            if (LoadCustomSongs.Value)
            {
                SongLoader.CustomSongsToSongs();
            }
        }

        private void DoPatching()
        {
            var harmony = new Harmony("com.maxgamertyper1.audiocontroller");

            Patch(harmony, typeof(AudioManager), "OnMatchStarted", "OnMatchStartedPrefix", true, false);
            Patch(harmony, typeof(AudioSource), "Play", "OnSongStartPlaying", false, false);
        }

        private void OnDestroy()
        {
            Log($"Bye Bye From {PluginInfo.PLUGIN_GUID}");
        }
        public static Stream GetResourceStream(string path)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream("AudioController" + "." + path);
        }

        public static Texture2D GetTextureFromPng(string path) // this is chatgpt, idk what this is, i kind of get it though
        {
            using (Stream stream = GetResourceStream(path))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    byte[] imageData = ms.ToArray();

                    Texture2D tex = new Texture2D(400, 400, TextureFormat.RGBA32, false);
                    tex.name = path;
                    tex.LoadImage(imageData);
                    return tex;
                }
            }
        }

        void Update()
        {
            try
            {
                AudioSource player = AudioManager.Get().musicPlayer;
                if (!player.isPlaying && player.clip != null && !Patches.IsPaused)
                {
                    Patches.NextButtonClicked();
                }
            } catch { }
        }

        private void Patch(Harmony harmony, Type OriginalClass , string OriginalMethod, string PatchMethod, bool prefix, bool transpiler)
        {
            MethodInfo MethodToPatch = AccessTools.Method(OriginalClass, OriginalMethod); // the method to patch
            MethodInfo Patch = AccessTools.Method(typeof(Patches), PatchMethod);
            
            if (prefix)
            {
                harmony.Patch(MethodToPatch, new HarmonyMethod(Patch));
            }
            else
            {
                if (transpiler)
                {
                    harmony.Patch(MethodToPatch, null, null, new HarmonyMethod(Patch));
                } else
                {
                    harmony.Patch(MethodToPatch, null, new HarmonyMethod(Patch));
                }
            }
            Log($"Patched {OriginalMethod} in {OriginalClass.ToString()}");
        }
    }

    public class SongLoader : MonoBehaviour
    {
        public static void CustomSongsToSongs()
        {
            GameObject currentloaderobj = new GameObject("MP3Loader",typeof(SongLoader));
            SongLoader currentloader = currentloaderobj.GetComponent<SongLoader>();
            string[] files = Directory.GetFiles(AudioController.CustomSongsPath);

            foreach (string file in files)
            {
                if (!file.Contains("mp3"))
                {
                    continue;
                }
                Debug.Log($"Attempting Conversion of {file} to AudioClip");
                currentloader.StartCoroutine(currentloader.LoadMP3("file://" + file));
            }
        }
        // Coroutine to load the MP3 and get the AudioClip
        IEnumerator LoadMP3(string filePath)
        {
            // Create a UnityWebRequest to load the MP3 file
            UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(filePath, AudioType.MPEG);

            // Send the request and wait for it to complete
            yield return www.SendWebRequest();

            // Check for errors in the request (cast to UnityWebRequest to access result)
            if (www.isNetworkError || www.isHttpError)
            {
                Debug.LogError("Error loading MP3: " + www.error);
            }
            else
            {
                // Successfully loaded the audio, get the AudioClip from the response
                AudioClip newaudio = DownloadHandlerAudioClip.GetContent(www);
                string actualfile = filePath.Replace(AudioController.CustomSongsPath + "\\", "").Replace("file://", "file:");
                newaudio.name = actualfile;
                Patches.CustomSongs.Add(newaudio);

                // At this point, loadedAudioClip is the AudioClip you can use
                Debug.Log($"{actualfile} Loaded Successfully!");
            }
        }
    }

    public class Patches
    {
        public static AudioManager AudioManager;
        public static bool SliderActive = false;
        public static GameObject SliderObject;
        public static TextMeshProUGUI SongText;
        public static TextMeshProUGUI VolumeText;
        public static TextMeshProUGUI ShuffleText;
        public static Image PauseButtonImage;
        public static bool? ShuffleButtonState;
        public static List<AudioClip> CustomSongs = new List<AudioClip>();
        public static bool HasLoadedSongs = false;
        public static int CurrentIndex = 0;
        public static List<int> SongQueue = null;
        public static List<int> PrevSongQueue = null;
        public static bool Looping = true;
        public static TextMeshProUGUI LoopText;
        public static bool IsPaused = false;
        public static bool IsOpened = true;

        public static void ChangeIndex()
        {
            Song[] SongArray = (AudioManager.test_songs != null && AudioManager.test_songs.Length != 0) ? AudioManager.test_songs : AudioManager.songs;
            if (CurrentIndex <= 0)
            {
                CurrentIndex += SongArray.Length;
            }
            CurrentIndex = CurrentIndex % SongArray.Length;
            AudioManager.currentSongIndex = CurrentIndex;
        }
        public static void LoopButtonClicked()
        {
            Looping = !Looping;
            AudioManager.musicPlayer.loop = Looping;
            LoopText.text = Looping ? "True" : "False";
        }

        public static void SwitchButtonClicked()
        {
            IsOpened = !IsOpened;
            VolumeText.transform.parent.gameObject.SetActive(IsOpened);
        }
        public static void NextButtonClicked()
        {
            AudioManager.musicPlayer.loop = false;
            AudioManager.introMusicPlayer.loop = false;
            if (!IsPaused) { PauseButtonClicked(); }
            if (ShuffleButtonState == null)  // normal shuffle
            {
                CurrentIndex++;
                ChangeIndex();
                AudioManager.StartMusic(AudioManager.currentSongIndex);
            } else if (ShuffleButtonState == true) // queue shuffle
            {
                int randomIndex = UnityEngine.Random.Range(0, SongQueue.Count);
                int nextsong = SongQueue[randomIndex];
                SongQueue.RemoveAt(randomIndex);
                PrevSongQueue.Insert(0,CurrentIndex);
                CurrentIndex = nextsong;
                ChangeIndex();
                AudioManager.StartMusic(AudioManager.currentSongIndex);
                if (SongQueue.Count == 0)
                {
                    SongQueue = Enumerable.Range(0, AudioManager.songs.Length).ToList();
                }
                if (PrevSongQueue.Count >= AudioManager.songs.Length * 4)
                {
                    PrevSongQueue.RemoveAt(PrevSongQueue.Count-1);
                }
            } else // random shuffle
            {
                int randomIndex = UnityEngine.Random.Range(0, AudioManager.songs.Length);
                Song nextsong = AudioManager.songs[randomIndex];
                PrevSongQueue.Insert(0, randomIndex);
                CurrentIndex = randomIndex;
                ChangeIndex();
                AudioManager.StartMusic(AudioManager.currentSongIndex);
                if (PrevSongQueue.Count >= AudioManager.songs.Length * 4)
                {
                    PrevSongQueue.RemoveAt(PrevSongQueue.Count - 1);
                }
            }
        }
        public static void PrevButtonClicked()
        {
            
            if (!IsPaused) { PauseButtonClicked(); }
            if (ShuffleButtonState == null)  // normal shuffle
            {
                CurrentIndex--;
                ChangeIndex();
                AudioManager.StartMusic(AudioManager.currentSongIndex);
            }
            else if (ShuffleButtonState == true) // queue shuffle
            {
                try
                {
                    int PrevIndex = PrevSongQueue[0];
                    PrevSongQueue.RemoveAt(0);
                    SongQueue.Insert(0, CurrentIndex);
                    CurrentIndex = PrevIndex;
                    ChangeIndex();
                    AudioManager.StartMusic(AudioManager.currentSongIndex);
                    if (PrevSongQueue.Count == 0)
                    {
                        CurrentIndex--;
                        ChangeIndex();
                        AudioManager.StartMusic(AudioManager.currentSongIndex);
                    }
                    if (SongQueue.Count >= AudioManager.songs.Length)
                    {
                        SongQueue.RemoveAt(SongQueue.Count - 1);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("No More Previous Song Data");
                }
                
            }
            else // random shuffle
            {
                try
                {
                    int PrevIndex = PrevSongQueue[0];
                    PrevSongQueue.RemoveAt(0);
                    CurrentIndex = PrevIndex;
                    ChangeIndex();
                    AudioManager.StartMusic(AudioManager.currentSongIndex);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("No More Previous Song Data");
                }
            }
        }
        public static void VolumeButtonClicked()
        {
            if (SliderActive)
            {
                SliderObject.SetActive(false);
                VolumeText.gameObject.SetActive(false);
            } else
            {
                SliderObject.SetActive(true);
                VolumeText.gameObject.SetActive(true);
                int Volume;
                try
                {
                    Volume = AudioManager.settings.MusicVolume;
                }
                catch (Exception e)
                {
                    Volume = 10;
                }
                SliderObject.GetComponent<Slider>().value = Volume;
            }
            SliderActive = !SliderActive;
        }
        public static void SliderChanged(float value)
        {
            AudioManager.settings.MusicVolume = (int)value;
            AudioManager.currentMusicVolume = ((int)value==0)?0 : AudioManager.currentMusicVolume;
            AudioManager.UpdateMusicMasterVolume();
            VolumeText.text = value.ToString();
        }
        public static void PauseButtonClicked()
        {
            IsPaused = !IsPaused;
            if (IsPaused)
            {
                AudioManager.PauseMusic();
                Texture2D PlayTexture = AudioController.GetTextureFromPng("play.png");
                Sprite PlaySprite = Sprite.Create(PlayTexture, new Rect(0, 0, PlayTexture.width, PlayTexture.height), new Vector2(0.5f, 0.5f));
                PlaySprite.name = "play.png";
                PauseButtonImage.sprite = PlaySprite;
            } else
            {
                AudioManager.UnPauseMusic();
                Texture2D PauseTexture = AudioController.GetTextureFromPng("pause.png");
                Sprite PauseSprite = Sprite.Create(PauseTexture, new Rect(0, 0, PauseTexture.width, PauseTexture.height), new Vector2(0.5f, 0.5f));
                PauseSprite.name = "pause.png";
                PauseButtonImage.sprite = PauseSprite;
            }
        }
        public static void ShuffleButtonClicked()
        {
            Debug.Log("Changing Shuffle");
            if (PrevSongQueue == null)
            {
                PrevSongQueue = new List<int>();
            }
            if (ShuffleButtonState == null)
            { // normal
                ShuffleButtonState = true;
                ShuffleText.text = "Queue";
                if (SongQueue == null)
                {
                    SongQueue = Enumerable.Range(0, AudioManager.songs.Length).ToList();
                }
            } else if (ShuffleButtonState == true) 
            { // queue shuffle
                ShuffleText.text = "Random";
                ShuffleButtonState = false;
            } else
            { // random shuffle
                ShuffleText.text = "None";
                ShuffleButtonState = null;
            }
        }
        public static GameObject CreateGameObject(string name,Vector3 localposition, List<Type> extratypes)
        {
            if (extratypes==null) {  extratypes = new List<Type>(); }
            if (!extratypes.Contains(typeof(RectTransform)))
                extratypes.Add(typeof(RectTransform));
            GameObject NewGameObject = new GameObject(name, extratypes.ToArray());
            RectTransform NewGameObjectRectTransform = NewGameObject.GetComponent<RectTransform>();
            NewGameObjectRectTransform.localPosition = localposition;
            return NewGameObject;
        }
        public static void AddImageInformation(GameObject gameObject,Vector2 SizeDelta, Vector3 LocalScale, string imagepath)
        {
            RectTransform gameObjectRectTransform = gameObject.GetComponent<RectTransform>();
            gameObjectRectTransform.sizeDelta = SizeDelta;
            gameObjectRectTransform.localScale = LocalScale;

            Image gameObjectImg = gameObject.GetComponent<Image>();
            Texture2D ImageTexture = AudioController.GetTextureFromPng(imagepath);
            Sprite ImageSprite = Sprite.Create(ImageTexture, new Rect(0, 0, ImageTexture.width, ImageTexture.height), new Vector2(0.5f, 0.5f));
            ImageSprite.name = imagepath;
            gameObjectImg.sprite = ImageSprite;
        }

        public static TextMeshProUGUI AddTextInformation(GameObject gameObject, Vector3 SizeDelta, string text)
        {
            // set size and pos
            RectTransform gameObjectRectTransform = gameObject.GetComponent<RectTransform>();
            gameObjectRectTransform.sizeDelta = SizeDelta;

            TextMeshProUGUI gameObjectText = gameObject.GetComponent<TextMeshProUGUI>();
            gameObjectText.fontStyle = FontStyles.Bold;
            gameObjectText.font = LocalizedText.localizationTable.GetFont(Settings.Get().Language, false);
            gameObjectText.text = text;
            gameObjectText.fontSize = 32;
            gameObjectText.alignment = TextAlignmentOptions.Center;

            return gameObjectText;
        }
        public static void CreateUIElements()
        {
            GameObject canvasObject = new GameObject("AudioControllerCanvas",typeof(CanvasScaler),typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.gameObject.name = "AudioControllerCanvas";

            // folder of items
            GameObject RadioFolder = CreateGameObject("RadioFolder", new Vector3(-810, -390, 0), null);
            RadioFolder.GetComponent<RectTransform>().SetParent(canvas.transform, false);

            if (!IsOpened)
            {
                RadioFolder.SetActive(false);
            }

            // make the background
            GameObject RadioBackground = CreateGameObject("RadioBackground", new Vector3(156, 31, 0), [typeof(CanvasRenderer), typeof(Image)]);
            RadioBackground.GetComponent<RectTransform>().SetParent(RadioFolder.transform, false);

            AddImageInformation(RadioBackground, new Vector2(700, 200), Vector3.one, "slider.png");
            RadioBackground.GetComponent<Image>().color = new Color(1, 1, 1, .5f);


            GameObject SwitchButton = CreateGameObject("SwitchButton", new Vector3(-560, -330, 0), new List<Type>([typeof(CanvasRenderer), typeof(Image), typeof(Button)]));
            SwitchButton.GetComponent<RectTransform>().SetParent(canvas.transform, false);

            AddImageInformation(SwitchButton, new Vector2(60, 20), Vector3.one, "slider.png");

            Button SwitchButtonActual = SwitchButton.GetComponent<Button>();
            SwitchButtonActual.onClick.AddListener(() => SwitchButtonClicked());


            // make previous button
            GameObject PrevButton = CreateGameObject("PrevButton", Vector3.zero, new List<Type>([typeof(CanvasRenderer), typeof(Image), typeof(Button)]));
            PrevButton.GetComponent<RectTransform>().SetParent(RadioFolder.transform, false);

            AddImageInformation(PrevButton, new Vector2(100, 100), new Vector3(.75f, .75f, .75f), "prev.png");

            Button PrevButtonActual = PrevButton.GetComponent<Button> ();
            PrevButtonActual.onClick.AddListener(() => PrevButtonClicked());



            // make next button
            GameObject NextButton = CreateGameObject("NextButton", new Vector3(200, 0, 0), new List<Type>([typeof(CanvasRenderer), typeof(Image), typeof(Button)]));
            NextButton.GetComponent<RectTransform>().SetParent(RadioFolder.transform, false);

            AddImageInformation(NextButton, new Vector2(100, 100), new Vector3(.75f, .75f, .75f), "next.png");

            Button NextButtonActual = NextButton.GetComponent<Button>();
            NextButtonActual.onClick.AddListener(() => NextButtonClicked());



            // make pause button
            GameObject PauseButton = CreateGameObject("PauseButton", new Vector3(100, 0, 0), new List<Type>([typeof(CanvasRenderer), typeof(Image), typeof(Button)]));
            PauseButton.GetComponent<RectTransform>().SetParent(RadioFolder.transform, false);

            string imagefile = !IsPaused ? "pause.png" : "play.png";

            AddImageInformation(PauseButton, new Vector2(100, 100), Vector3.one, imagefile);

            Button PauseButtonActual = PauseButton.GetComponent<Button>();
            PauseButtonActual.onClick.AddListener(() => PauseButtonClicked());

            PauseButtonImage = PauseButton.GetComponent<Image>();


            // make volume button
            GameObject VolumeButton = CreateGameObject("VolumeButton", new Vector3(-100, 0, 0), new List<Type>([typeof(CanvasRenderer), typeof(Image), typeof(Button)]));
            VolumeButton.GetComponent<RectTransform>().SetParent(RadioFolder.transform, false);

            AddImageInformation(VolumeButton, new Vector2(100, 100), Vector3.one, "volumeicon.png");

            Button VolumeIconActual = VolumeButton.GetComponent<Button>();
            VolumeIconActual.onClick.AddListener(() => VolumeButtonClicked());


            // make shuffle button
            GameObject ShuffleButton = CreateGameObject("ShuffleButton", new Vector3(300, 0, 0), new List<Type>([typeof(CanvasRenderer), typeof(Image), typeof(Button)]));
            ShuffleButton.GetComponent<RectTransform>().SetParent(RadioFolder.transform, false);

            AddImageInformation(ShuffleButton, new Vector2(100, 100), Vector3.one, "shuffle.png");

            Button ShuffleButtonActual = ShuffleButton.GetComponent<Button>();
            ShuffleButtonActual.onClick.AddListener(() => ShuffleButtonClicked());



            // make loop button
            GameObject LoopButton = CreateGameObject("LoopButton", new Vector3(410, 0, 0), new List<Type>([typeof(CanvasRenderer), typeof(Image), typeof(Button)]));
            LoopButton.GetComponent<RectTransform>().SetParent(RadioFolder.transform, false);

            AddImageInformation(LoopButton, new Vector2(100, 100), Vector3.one, "loop.png");

            Button LoopButtonActual = LoopButton.GetComponent<Button>();
            LoopButtonActual.onClick.AddListener(() => LoopButtonClicked());
            LoopButton.transform.localScale = new Vector2(.9f, .9f);


            // make currently playing text
            GameObject PlayingText = CreateGameObject("PlayingText", new Vector3(150, 95, 0), new List<Type>([typeof(TextMeshProUGUI)]));
            PlayingText.GetComponent<RectTransform>().SetParent(RadioFolder.transform, false);

            AddTextInformation(PlayingText, new Vector3(350, 50, 0), "Currently Playing: ");

            // make shuffle text
            GameObject ShuffleText = CreateGameObject("ShuffleText", new Vector3(300, -50, 0), new List<Type>([typeof(TextMeshProUGUI)]));
            ShuffleText.GetComponent<RectTransform>().SetParent(RadioFolder.transform, false);

            Patches.ShuffleText = AddTextInformation(ShuffleText, new Vector3(350, 50, 0), "None");

            if (ShuffleButtonState == null)
            { // normal
                Patches.ShuffleText.text = "None";
            }
            else if (ShuffleButtonState == true)
            { // queue shuffle
                Patches.ShuffleText.text = "Queue";
            }
            else
            { // random shuffle
                Patches.ShuffleText.text = "Random";
            }

            // make loop text
            GameObject LoopText = CreateGameObject("LoopText", new Vector3(410, -50, 0), new List<Type>([typeof(TextMeshProUGUI)]));
            LoopText.GetComponent<RectTransform>().SetParent(RadioFolder.transform, false);

            Patches.LoopText = AddTextInformation(LoopText, new Vector3(350, 50, 0), "True");
            Patches.LoopText.text = Looping ? "True" : "False";

            // make song text
            GameObject SongText = CreateGameObject("SongText", new Vector3(150, 57, 0), new List<Type>([typeof(TextMeshProUGUI)]));
            SongText.GetComponent<RectTransform>().SetParent(RadioFolder.transform, false);

            Patches.SongText = AddTextInformation(SongText, new Vector3(600, 50, 0), "<Song Playing>");
            Patches.SongText.overflowMode = TextOverflowModes.Ellipsis;

            // make volume text
            GameObject VolumeText = CreateGameObject("VolumeText", new Vector3(-100, 375, 0), new List<Type>([typeof(TextMeshProUGUI)]));
            VolumeText.GetComponent<RectTransform>().SetParent(RadioFolder.transform, false);

            Patches.VolumeText = AddTextInformation(VolumeText, new Vector3(350, 50, 0), "10");

            VolumeText.SetActive(false);


            // Create Slider
            GameObject SliderObject = new GameObject("VolumeSlider", typeof(RectTransform), typeof(Slider), typeof(Image));
            SliderObject.transform.SetParent(RadioFolder.transform, false);

            Patches.SliderObject = SliderObject;

            AddImageInformation(SliderObject, new Vector2(300, 30), Vector3.one, "sliderback.png");

            // Setup Slider
            Slider Slider = SliderObject.GetComponent<Slider>();
            Slider.minValue = 0;
            Slider.maxValue = 20;
            int Volume;
            try
            {
                Volume = AudioManager.settings.MusicVolume;
            } catch (Exception e) {
                Volume = 10;
            }
            Slider.value = Volume;
            Slider.wholeNumbers = true;

            // Set RectTransform
            RectTransform SliderObjectRect = SliderObject.GetComponent<RectTransform>();
            SliderObjectRect.localPosition = new Vector3(-100, 200, 0);
            SliderObjectRect.rotation = Quaternion.Euler(0, 0, 90);

            // Optional: Listen to value change
            Slider.onValueChanged.AddListener((val) => { SliderChanged(val); });

            SliderObject.SetActive(false);

            // Create Fill Area
            GameObject FillArea = new GameObject("Fill Area", typeof(RectTransform));
            FillArea.transform.SetParent(SliderObject.transform, false);

            RectTransform FillRect = FillArea.GetComponent<RectTransform>();
            FillRect.anchorMin = new Vector2(0, 0.25f);
            FillRect.anchorMax = new Vector2(1, 0.75f);
            FillRect.offsetMin = FillRect.offsetMax = new Vector2(1,0);
            
            // Create Fill Image
            GameObject Fill = new GameObject("Fill", typeof(Image));
            Fill.transform.SetParent(FillArea.transform, false);

            AddImageInformation(Fill, new Vector2(-20,0), Vector3.one, "slider.png");

            RectTransform FillImageRect = Fill.GetComponent<RectTransform>();
            FillImageRect.anchorMin = Vector2.zero;
            FillImageRect.anchorMax = Vector2.one;
            FillImageRect.offsetMin = new Vector2(10, 0);
            FillImageRect.offsetMax = new Vector2(-10, 0);
            Slider.fillRect = FillImageRect;

            
            // Create Handle
            GameObject handle = new GameObject("Handle", typeof(Image));
            handle.transform.SetParent(SliderObject.transform, false);
            Image handleImage = handle.GetComponent<Image>();
            handleImage.color = new Color32(4,65,107,255);

            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(10, 20);
            handleRect.anchorMin = new Vector2(0.5f, 0.5f);
            handleRect.anchorMax = new Vector2(0.5f, 0.5f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            Slider.targetGraphic = handleImage;
            Slider.handleRect = handleRect;


            RadioFolder.transform.localScale = new Vector2(.6f, .6f);
        }

        static AudioClip GenerateQuietClip(float duration, float frequency, float amplitude)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.FloorToInt(duration * sampleRate);

            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)sampleRate;
                samples[i] = amplitude * Mathf.Sin(2 * Mathf.PI * frequency * time);
            }

            AudioClip clip = AudioClip.Create("QuietSound", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public static void OnMatchStartedPrefix(ref AudioManager __instance)
        {
            IsPaused = false;
            CreateUIElements();
            AudioManager = AudioManager.Get();
            if (HasLoadedSongs) { return; }
            if (AudioController.OverrideOtherSongs.Value && AudioController.LoadCustomSongs.Value)
            {
                AudioManager.songs = [];
            }
            List<Song> newsonglist = new List<Song>(AudioManager.songs);
            foreach (AudioClip CustomAudio in CustomSongs)
            {
                Song NewCustomSong = new Song();
                NewCustomSong.name = CustomAudio.name;
                NewCustomSong.songLoop = CustomAudio;
                NewCustomSong.intro = Patches.GenerateQuietClip(.1f,100f,0.0000000001f);
                NewCustomSong.pitch = 1;
                newsonglist.Add(NewCustomSong);
            }
            AudioManager.songs = newsonglist.ToArray();
            HasLoadedSongs = true;
        }
        public static void OnSongStartPlaying(ref AudioSource __instance)
        {
            AudioManager am = AudioManager.Get();
            am.musicPlayer.loop = Looping;
            try {
                SongText.text = am.songs[am.currentSongIndex].name;
            } catch {
                Debug.Log("Couldn't change text");
            }
        }
    }
}

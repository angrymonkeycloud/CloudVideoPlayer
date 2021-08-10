﻿using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AngryMonkey.Cloud.Components
{
	public partial class VideoPlayer
	{
		private ElementReference ComponentElement { get; set; }

		private Task<IJSObjectReference> _module;
		private Task<IJSObjectReference> Module => _module ??= GeneralMethods.GetIJSObjectReference(jsRuntime, "videoplayer/videoplayer.js");

		private string ClassAttributes { get; set; } = string.Empty;

		private bool IsUserChangingProgress = false;
		private bool IsVideoPlaying = false;
		private bool IsFullScreen = false;
		private bool IsMuted = false;
		private bool DoShowVolumeControls = false;
		private bool IsSeeking = false;
		private bool ShowSeekingInfo = false;

		private Dictionary<string, string> VideoSettingsInfo
		{
			get
			{
				Dictionary<string, string> info = new();

				if (!string.IsNullOrEmpty(Title))
					info.Add("Title", Title);

				if (CurrentVideoInfo != null)
				{
					info.Add("Duration", GetTime(CurrentVideoInfo.Duration));
					info.Add("Aspect Ratio", CurrentVideoInfo.DisplayAspectRatio);
				}

				info.Add("Status", Status.ToString());

				return info;
			}
		}

		private VideoInfo CurrentVideoInfo { get; set; }

		private bool IsUserInteracting = false;

		private bool HideControls => IsVideoPlaying && !IsUserInteracting && !IsUserChangingProgress && !ShowSideBar;

		private void Repaint()
		{
			ProgressBarStyle = HideControls ? ProgressBarStyle.Flat : ProgressBarStyle.Circle;

			List<string> attributes = new();

			if (HideControls)
			{
				DoShowVolumeControls = false;
				attributes.Add("_hidecontrols");
			}

			if (IsVideoPlaying)
				attributes.Add("_playing");

			if (IsFullScreen)
				attributes.Add("_fullscreen");

			if (ShowSeekingInfo)
				attributes.Add("_showseekinginfo");

			ClassAttributes = string.Join(' ', attributes);
		}

		[Parameter]
		public string Title { get; set; }

		[Parameter]
		public bool Loop { get; set; } = false;

		private string DisplayLoop => Loop ? "On" : "Off";

		[Parameter]
		public string VideoUrl { get; set; }

		[Parameter]
		public double Volume { get; set; } = 1;

		private string DisplayVolume
		{
			get
			{
				return $"{Volume * 100}";
			}
		}

		public double CurrentTime { get; set; } = 0;

		private ProgressBarStyle ProgressBarStyle = ProgressBarStyle.Circle;

		[Parameter]
		public Action<VideoState> TimeUpdate { get; set; }

		[Parameter]
		public EventCallback<VideoState> TimeUpdateEvent { get; set; }
		bool TimeUpdateRequired => TimeUpdate is object;
		bool TimeUpdateEventRequired => TimeUpdateEvent.HasDelegate;
		bool EventFiredEventRequired => EventFiredEvent.HasDelegate;
		bool EventFiredRequired => EventFired is object;
		[Parameter] public Action<VideoEventData> EventFired { get; set; }
		[Parameter] public EventCallback<VideoEventData> EventFiredEvent { get; set; }

		[Parameter]
		public Dictionary<VideoEvents, VideoStateOptions> VideoEventOptions { get; set; }
		bool RegisterEventFired => EventFiredEventRequired || EventFiredRequired;

		[Parameter]
		public VideoPlayerSettings Settings { get; set; }

		private Guid latestId = Guid.Empty;

		#region Volume Methods

		public async Task MuteVolume()
		{
			IsMuted = true;

			DoShowVolumeControls = false;

			var module = await Module;

			await module.InvokeVoidAsync("muteVolume", ComponentElement, IsMuted);
		}

		private async Task OnVolumeButtonClick(MouseEventArgs args)
		{
			if (IsMuted)
			{
				IsMuted = false;

				var module = await Module;

				await module.InvokeVoidAsync("muteVolume", ComponentElement, IsMuted);
			}
			else DoShowVolumeControls = !DoShowVolumeControls;
		}

		protected async Task OnVolumeChanging(ProgressBarChangeEventArgs args)
		{
			Volume = args.NewValue;

			var module = await Module;

			await module.InvokeVoidAsync("changeVolume", ComponentElement, Volume);

			await ProgressiveDelay();
		}

		protected async Task OnVolumeChanged(ProgressBarChangeEventArgs args)
		{
			DoShowVolumeControls = false;

			if (Convert.ToDouble(args.NewValue) == 0)
			{
				IsMuted = true;
				Volume = 1;
			}
		}

		#endregion

		#region Time / Duration

		private string DisplayTimeDuration => $"{GetTime(CurrentTime)} / {GetTime(CurrentVideoInfo?.Duration ?? 0)}";

		public double SeekInfoTime { get; set; }
		private string DisplaySeekInfoTime => GetTime(SeekInfoTime);

		private string GetTime(double seconds)
		{
			TimeSpan time = TimeSpan.FromSeconds(seconds);
			int timeLevel = GetTimeLevel();

			string result = $"{time:ss}";

			if (timeLevel > 0)
			{
				result = $"{time:mm}:{result}";

				if (timeLevel > 1)
				{
					result = $"{time:hh}:{result}";

					if (timeLevel > 2)
						result = $"{time:dd}:{result}";
				}
			}

			return result[0] == '0' ? result.Remove(0, 1) : result;
		}

		private int GetTimeLevel()
		{
			TimeSpan time = TimeSpan.FromSeconds(CurrentVideoInfo?.Duration ?? 0);

			if (time.TotalMinutes < 1)
				return 0;

			if (time.TotalHours < 1)
				return 1;

			if (time.TotalDays < 1)
				return 2;

			return 3;
		}

		#endregion

		#region Settings Menu

		private bool ShowSideBar = false;
		private bool ShowSideBarInfo = false;
		private bool ShowSideBarPlaybackSpeed = false;
		private bool ShowSideBarLoop = false;
		private bool ShowSideBarMenu => !ShowSideBarInfo && !ShowSideBarPlaybackSpeed && !ShowSideBarLoop;

		private double PlaybackSpeed = 1;
		private Dictionary<double, string> PlaybackSpeedOptions = new() { { 0.25, "0.25" }, { 0.5, "0.5" }, { 0.75, "0.75" }, { 1, "Normal" }, { 1.25, "1.25" }, { 1.5, "1.5" }, { 1.75, "1.75" }, { 2, "2" } };
		private string DisplayPlaybackSpeed => PlaybackSpeedOptions[PlaybackSpeed];

		private async Task ChangePlaybackSpeed(double newSpeed)
		{
			PlaybackSpeed = newSpeed;

			var module = await Module;

			await module.InvokeVoidAsync("setVideoPlaybackSpeed", ComponentElement, PlaybackSpeed);

			ShowSideBarPlaybackSpeed = false;
		}

		public void ResetSettingsMenu()
		{
			ShowSideBarInfo = false;
			ShowSideBarPlaybackSpeed = false;
			ShowSideBarLoop = false;
		}

		public async Task MoreButtonInfo()
		{
			ResetSettingsMenu();
			ShowSideBar = !ShowSideBar;
		}

		public void ShowVideoInfo()
		{
			ShowSideBarInfo = true;
		}

		public void ShowVideoPlaybackSpeedOptions()
		{
			ShowSideBarPlaybackSpeed = true;
		}

		public void ShowVideoLoop()
		{
			ShowSideBarLoop = true;
		}

		protected void ChangeLoop()
		{
			Loop = !Loop;

			ShowSideBarLoop = false;
		}

		#endregion

		protected async Task OnProgressMouseDown(MouseEventArgs args)
		{
			IsUserChangingProgress = true;
			await ProgressiveDelay();
		}

		protected async Task OnProgressTouchStart(TouchEventArgs args)
		{
			IsUserChangingProgress = true;
			await ProgressiveDelay();
		}

		protected async Task OnProgressChanged(ProgressBarChangeEventArgs args)
		{
			IsSeeking = false;
			ShowSeekingInfo = false;

			Repaint();

			if (args.PreviousValue.HasValue)
			{
				double durationDifference = args.NewValue - args.PreviousValue.Value;

				if (durationDifference > -1 && durationDifference < 1)
					return;
			}

			var module = await Module;

			await module.InvokeVoidAsync("changeCurrentTime", ComponentElement, args.NewValue);

			IsUserChangingProgress = false;

			if (CurrentTime == CurrentVideoInfo.Duration)
				await StopVideo();

			await ProgressiveDelay();
		}

		protected async Task OnProgressChanging(ProgressBarChangeEventArgs args)
		{
			IsSeeking = true;
			ShowSeekingInfo = true;
			Repaint();
			SeekInfoTime = args.NewValue;

			var module = await Module;
			await module.InvokeVoidAsync("seeking", ComponentElement, SeekInfoTime, CurrentVideoInfo.Duration);
		}

		protected async Task OnProgressMouseMove(MouseEventArgs args)
		{
			if (IsSeeking)
				return;

			ShowSeekingInfo = true;
			Repaint();

			var module = await Module;

			double newValue = await module.InvokeAsync<double>("seeking", ComponentElement, args.ClientX);

			if (newValue < 0)
				return;

			SeekInfoTime = newValue;
		}

		protected async Task OnProgressMouseOut(MouseEventArgs args)
		{
			if (IsSeeking)
				return;

			ShowSeekingInfo = false;
			Repaint();
		}

		public async Task OnVideoChange(ChangeEventArgs args)
		{
			VideoEventData eventData = JsonSerializer.Deserialize<VideoEventData>((string)args.Value);

			IsVideoPlaying = !eventData.State.Paused;
			Repaint();

			switch (eventData.EventName)
			{
				case VideoEvents.LoadedMetadata:
					do
					{
						await VideoLoaded();

						if (CurrentVideoInfo == null)
							await Task.Delay(200);

						Status = VideoStatus.Stoped;

					} while (CurrentVideoInfo == null);
					break;

				case VideoEvents.TimeUpdate:

					if (!IsUserChangingProgress)
					{
						CurrentTime = eventData.State.CurrentTime;

						if (CurrentTime == CurrentVideoInfo.Duration)
						{
							await StopVideo();

							if (Loop)
								await PlayVideo();
						}
					}
					break;

				case VideoEvents.Waiting:
					Status = VideoStatus.Buffering;
					break;

				case VideoEvents.Playing:
					Status = VideoStatus.Playing;
					break;

				case VideoEvents.Play:
					Status = VideoStatus.Playing;
					break;

				case VideoEvents.Pause:
					Status = CurrentTime == 0 ? VideoStatus.Stoped : VideoStatus.Paused;
					break;

				default: break;
			}
		}

		private VideoStatus Status { get; set; } = VideoStatus.Loading;

		private enum VideoStatus
		{
			Loading,
			Playing,
			Paused,
			Stoped,
			Buffering,
			Unknown
		}

		private bool _isEmptyTouched = false;
		private bool _forceHideControls = false;

		protected async Task OnEmptyTouch(TouchEventArgs args)
		{
			_isEmptyTouched = true;

			if (IsVideoPlaying && !HideControls)
			{
				_forceHideControls = true;
				IsUserInteracting = false;
				Repaint();
			}
		}

		protected async Task OnEmptyClick(MouseEventArgs args)
		{
			if (ShowSideBar == true)
			{
				if (ShowSideBarMenu)
					ShowSideBar = false;
				else ResetSettingsMenu();

				return;
			}

			if (_isEmptyTouched)
			{
				_isEmptyTouched = false;

				return;
			}

			if (IsVideoPlaying)
				await PauseVideo();
			else await PlayVideo();
		}

		protected override async Task OnAfterRenderAsync(bool firstRender)
		{
			if (firstRender)
			{
				var module = await Module;

				await module.InvokeVoidAsync("init", ComponentElement);

				await Implement(VideoEvents.TimeUpdate);
				await Implement(VideoEvents.Play);
				await Implement(VideoEvents.Playing);
				await Implement(VideoEvents.Pause);
				await Implement(VideoEvents.Waiting);
				await Implement(VideoEvents.LoadedMetadata);
			}
		}

		async Task Implement(VideoEvents eventName)
		{
			VideoStateOptions options = new() { All = true };
			VideoEventOptions?.TryGetValue(eventName, out options);

			var module = await Module;

			await module.InvokeVoidAsync("registerCustomEventHandler", ComponentElement, eventName.ToString().ToLower(), options.GetPayload());
		}

		public async Task VideoLoaded()
		{
			var module = await Module;

			CurrentVideoInfo = await module.InvokeAsync<VideoInfo>("getVideoInfo", ComponentElement);
		}

		public async Task PlayVideo()
		{
			if (CurrentVideoInfo == null)
				await VideoLoaded();

			var module = await Module;

			await module.InvokeVoidAsync("play", ComponentElement);
		}

		public async Task PauseVideo()
		{
			var module = await Module;

			await module.InvokeVoidAsync("pause", ComponentElement);
		}

		public async Task EnterFullScreen()
		{
			var module = await Module;

			await module.InvokeVoidAsync("enterFullScreen", ComponentElement);
		}

		public async Task ExitFullScreen()
		{
			var module = await Module;

			await module.InvokeVoidAsync("exitFullScreen", ComponentElement);
		}

		public async Task OnFullScreenChange(EventArgs args)
		{
			IsFullScreen = !IsFullScreen;

			Repaint();
		}

		public async Task StopVideo()
		{
			CurrentTime = 0;

			var module = await Module;

			await module.InvokeVoidAsync("stop", ComponentElement);

			Repaint();
		}

		public async ValueTask DisposeAsync()
		{
			if (_module != null)
			{
				var module = await _module;
				await module.DisposeAsync();
			}
		}

		protected override async void OnParametersSet()
		{
			base.OnParametersSet();
		}

		protected async Task OnMouseWheel(WheelEventArgs args)
		{
			if (DoShowVolumeControls)
			{
				double newValue;

				if (args.DeltaY < 0)
					newValue = Volume <= .9 ? Volume + .1 : 1;
				else
					newValue = Volume >= .1 ? Volume - .1 : 0;

				newValue = Math.Round(newValue, 1);

				await OnVolumeChanging(new ProgressBarChangeEventArgs() { NewValue = newValue });
			}

			await ProgressiveDelay();
		}

		private async Task OnComponentClick(MouseEventArgs args)
		{
			if (_forceHideControls)
			{
				_forceHideControls = false;
				return;
			}


			await ProgressiveDelay();
		}

		public async Task MainMouseMove(MouseEventArgs args)
		{
			if (_forceHideControls)
				return;

			await ProgressiveDelay();
		}

		private async Task ProgressiveDelay()
		{
			IsUserInteracting = true;

			Repaint();

			Guid id = Guid.NewGuid();
			latestId = id;

			await Task.Delay(3000);

			if (id != latestId)
				return;

			IsUserInteracting = false;
			Repaint();
		}
	}
}

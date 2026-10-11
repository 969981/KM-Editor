/* SPDX-License-Identifier: GPL-3.0-only */

import { GameplaySettingsSection, type GameplaySettingsSectionProps } from '../gameplay-settings/GameplaySettingsSection';
import './CameraControlSection.css';

export function CameraHookSection(props: Omit<GameplaySettingsSectionProps, 'cameraControl' | 'cameraHook'>) {
  return <div className="wide-panel camera-control"><GameplaySettingsSection {...props} cameraHook /></div>;
}

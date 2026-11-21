import torch
import torch.nn as nn
import torch.optim as optim
import numpy as np
import os

torch.manual_seed(42)
np.random.seed(42)

class AdaptiveTrafficPolicy(nn.Module):
    def __init__(self, input_dim=13, hidden_dim=96, num_classes=8, control_dim=2):
        super().__init__()
        self.shared = nn.Sequential(
            nn.Linear(input_dim, hidden_dim),
            nn.ReLU(),
            nn.Linear(hidden_dim, hidden_dim),
            nn.ReLU()
        )
        self.decision_head = nn.Linear(hidden_dim, num_classes)
        self.control_head = nn.Sequential(
            nn.Linear(hidden_dim, control_dim),
            nn.Tanh()
        )

    def forward(self, x):
        features = self.shared(x)
        logits = self.decision_head(features)
        control = self.control_head(features)
        return torch.cat([logits, control], dim=-1)

def main():
    print("=" * 75)
    print("HUẤN LUYỆN MÔ HÌNH AI: CHUYỂN LÀN, HỦY CHUYỂN LÀN & CREEP FORWARD CHỐNG KẸT")
    print("=" * 75)

    N = 180000
    speed = np.random.uniform(0.0, 1.0, N)
    gap = np.random.uniform(0.01, 1.0, N)
    rel_speed = np.random.uniform(-1.0, 1.0, N)
    target_speed = np.random.uniform(0.4, 0.9, N)
    speed_err = np.clip((target_speed - speed) / np.maximum(0.1, target_speed), -1.0, 1.0)
    road_offset = np.random.uniform(-0.8, 0.8, N)
    side_balance = np.random.uniform(-1.0, 1.0, N)
    lead_brake = (np.random.uniform(0.0, 1.0, N) < 0.25).astype(np.float32)
    pedestrian = (np.random.uniform(0.0, 1.0, N) < 0.15).astype(np.float32)
    density = np.random.uniform(0.0, 1.0, N)
    opposite_clear_gap = np.random.uniform(0.0, 1.0, N)
    is_overtaking_opp = (np.random.uniform(0.0, 1.0, N) < 0.20).astype(np.float32)
    is_changing_lane = (np.random.uniform(0.0, 1.0, N) < 0.30).astype(np.float32)
    adjacent_clear_gap = np.random.uniform(0.0, 1.0, N)

    is_changing_lane[is_overtaking_opp == 1.0] = 0.0

    decision = np.zeros(N, dtype=np.int64)

    emergency_mask = (gap < 0.04) | ((gap < 0.08) & (rel_speed < -0.4))
    decision[emergency_mask] = 4

    yield_mask = (~emergency_mask) & (pedestrian == 1.0) & (gap < 0.25)
    decision[yield_mask] = 3

    abort_mask = (
        (~emergency_mask) & (~yield_mask) &
        (is_changing_lane == 1.0) &
        ((adjacent_clear_gap < 0.20) | (speed < 0.12) | (np.abs(side_balance) > 0.75))
    )
    decision[abort_mask] = 6

    creep_mask = (
        (~emergency_mask) & (~yield_mask) & (~abort_mask) &
        (speed < 0.15) &
        (gap >= 0.05) & (gap <= 0.28) &
        (rel_speed >= -0.15) &
        (pedestrian == 0.0)
    )
    decision[creep_mask] = 7

    start_overtake_mask = (
        (~emergency_mask) & (~yield_mask) & (~abort_mask) & (~creep_mask) &
        (is_overtaking_opp == 0.0) & (is_changing_lane == 0.0) &
        (opposite_clear_gap > 0.65) &
        ((gap < 0.35) | (density > 0.40)) &
        (pedestrian == 0.0) &
        (adjacent_clear_gap < 0.45)
    )
    continue_overtake_mask = (
        (~emergency_mask) & (~yield_mask) & (~abort_mask) & (~creep_mask) &
        (is_overtaking_opp == 1.0)
    )
    overtake_mask = start_overtake_mask | continue_overtake_mask
    decision[overtake_mask] = 5

    evasion_mask = (
        (~emergency_mask) & (~yield_mask) & (~abort_mask) & (~creep_mask) & (~overtake_mask) &
        (is_overtaking_opp == 0.0) &
        (gap < 0.35) & (adjacent_clear_gap > 0.50) &
        (pedestrian == 0.0)
    )
    decision[evasion_mask] = 2

    follow_mask = (
        (~emergency_mask) & (~yield_mask) & (~abort_mask) & (~creep_mask) &
        (~overtake_mask) & (~evasion_mask) &
        (gap < 0.45)
    )
    decision[follow_mask] = 1

    free_mask = (
        (~emergency_mask) & (~yield_mask) & (~abort_mask) & (~creep_mask) &
        (~overtake_mask) & (~evasion_mask) & (~follow_mask)
    )
    decision[free_mask] = 0

    target_accel = np.zeros(N, dtype=np.float32)
    target_steer = -0.35 * road_offset

    target_accel[decision == 0] = np.clip(0.85 * speed_err[decision == 0], -0.2, 1.0)

    target_accel[decision == 1] = np.clip(
        1.2 * rel_speed[decision == 1] + 0.6 * (gap[decision == 1] - 0.22) - 0.3 * lead_brake[decision == 1],
        -0.85, 0.6
    )

    target_accel[decision == 2] = np.clip(0.35 + 0.4 * speed_err[decision == 2], 0.1, 0.7)
    target_steer[decision == 2] = np.sign(side_balance[decision == 2] + 1e-5) * 0.55

    target_accel[decision == 3] = -0.85
    target_steer[decision == 3] = 0.0

    target_accel[decision == 4] = -1.0
    target_steer[decision == 4] = 0.0

    opp_returning = (decision == 5) & (is_overtaking_opp == 1.0) & ((opposite_clear_gap < 0.50) | (pedestrian == 1.0) | (gap > 0.40))
    target_steer[opp_returning] = 0.65
    target_accel[opp_returning] = 0.2
    opp_cruising = (decision == 5) & (is_overtaking_opp == 1.0) & (~opp_returning)
    target_accel[opp_cruising] = 0.60
    target_steer[opp_cruising] = 0.0
    opp_starting = (decision == 5) & (is_overtaking_opp == 0.0)
    target_accel[opp_starting] = 0.50
    target_steer[opp_starting] = -0.60

    target_accel[decision == 6] = -0.25
    target_steer[decision == 6] = -1.0 * road_offset[decision == 6]

    target_accel[decision == 7] = np.clip(0.20 + 0.50 * (gap[decision == 7] - 0.05), 0.15, 0.40)
    target_steer[decision == 7] = -0.40 * road_offset[decision == 7]

    X = np.stack([
        speed, gap, rel_speed, speed_err, road_offset, side_balance,
        lead_brake, pedestrian, density, opposite_clear_gap, is_overtaking_opp,
        is_changing_lane, adjacent_clear_gap
    ], axis=1).astype(np.float32)

    Y_cls = decision
    Y_reg = np.stack([target_accel, target_steer], axis=1).astype(np.float32)

    tensor_X = torch.from_numpy(X)
    tensor_Y_cls = torch.from_numpy(Y_cls)
    tensor_Y_reg = torch.from_numpy(Y_reg)

    dataset = torch.utils.data.TensorDataset(tensor_X, tensor_Y_cls, tensor_Y_reg)
    dataloader = torch.utils.data.DataLoader(dataset, batch_size=256, shuffle=True)

    device = torch.device("cpu")
    model = AdaptiveTrafficPolicy(input_dim=13, hidden_dim=96, num_classes=8, control_dim=2).to(device)

    criterion_cls = nn.CrossEntropyLoss()
    criterion_reg = nn.MSELoss()
    optimizer = optim.Adam(model.parameters(), lr=0.0025, weight_decay=1e-5)
    scheduler = optim.lr_scheduler.CosineAnnealingLR(optimizer, T_max=15)

    epochs = 15
    for epoch in range(epochs):
        model.train()
        total_cls_loss = 0.0
        total_reg_loss = 0.0
        correct = 0
        total = 0

        for b_x, b_cls, b_reg in dataloader:
            b_x, b_cls, b_reg = b_x.to(device), b_cls.to(device), b_reg.to(device)
            optimizer.zero_grad()

            preds = model(b_x)
            pred_logits = preds[:, :8]
            pred_control = preds[:, 8:]

            loss_cls = criterion_cls(pred_logits, b_cls)
            loss_reg = criterion_reg(pred_control, b_reg)
            loss = loss_cls + 1.8 * loss_reg

            loss.backward()
            optimizer.step()

            total_cls_loss += loss_cls.item() * len(b_x)
            total_reg_loss += loss_reg.item() * len(b_x)

            _, predicted_labels = torch.max(pred_logits, 1)
            correct += (predicted_labels == b_cls).sum().item()
            total += len(b_x)

        scheduler.step()
        acc = (correct / total) * 100.0
        avg_cls = total_cls_loss / total
        avg_reg = total_reg_loss / total
        print(f"Epoch {epoch+1:02d}/{epochs:02d} - Độ chính xác phân loại: {acc:.2f}% | Cls Loss: {avg_cls:.4f} | Reg Loss: {avg_reg:.4f}", flush=True)

    model.eval()
    dummy_input = torch.randn(1, 13, requires_grad=False)
    onnx_output_path = r"E:\Unity Prj\TwoLaneSimulation\Assets\Models\traffic_aif_agent.onnx"

    torch.onnx.export(
        model,
        dummy_input,
        onnx_output_path,
        export_params=True,
        opset_version=13,
        do_constant_folding=True,
        input_names=["input"],
        output_names=["output"],
        dynamic_axes={
            "input": {0: "batch_size"},
            "output": {0: "batch_size"}
        }
    )

    print("=" * 75, flush=True)
    print(f"HUẤN LUYỆN & XUẤT ONNX THÀNH CÔNG: {onnx_output_path}", flush=True)
    print("=" * 75, flush=True)

if __name__ == "__main__":
    main()

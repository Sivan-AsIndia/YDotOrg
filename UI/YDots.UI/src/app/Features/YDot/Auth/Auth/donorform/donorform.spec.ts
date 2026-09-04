import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DonorformComponent } from './donorform';

describe('Donorform', () => {
  let component: DonorformComponent;
  let fixture: ComponentFixture<DonorformComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DonorformComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(DonorformComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
